using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Linteum.Infrastructure;

public class UserRepository : IUserRepository
{
    
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly IBalanceChangedEventRepository _balanceChangedEventRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly Config _defaultsConfig;
    private readonly ILogger<UserRepository> _logger;
    

    public UserRepository(AppDbContext context, IMapper mapper, IBalanceChangedEventRepository balanceChangedEventRepository, ISubscriptionRepository subscriptionRepository, Config defaultsConfig, ILogger<UserRepository> logger)
    {
        _context = context;
        _mapper = mapper;
        _balanceChangedEventRepository = balanceChangedEventRepository;
        _subscriptionRepository = subscriptionRepository;
        _defaultsConfig = defaultsConfig;
        _logger = logger;
    }

    public async Task<UserDto?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Email == email)
            .ProjectTo<UserDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
    }

    public async Task<UserDto?> GetByUserNameAsync(string userName)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.UserName == userName)
            .ProjectTo<UserDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
    }

    public async Task<UserDto?> GetByIdAsync(Guid id)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .ProjectTo<UserDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
    }

    //Is not suitable for large data, but used for ~10 users batches now.
    public async Task<string[]> GetByIdAsync(IList<Guid> id)
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => id.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToArrayAsync();

        return id
            .Select(requestedId => users.FirstOrDefault(u => u.Id == requestedId)?.UserName ?? string.Empty)
            .ToArray();
    }

    public async Task<UserDto?> AddOrUpdateUserAsync(UserDto userDto, UserPaswordDto? passwordDto = null)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var existingUser = (userDto.Id.HasValue && userDto.Id.Value != Guid.Empty) ?
                await _context.Users.FirstOrDefaultAsync(u => u.Id == userDto.Id) : 
                await _context.Users
                .FirstOrDefaultAsync(u => u.Email == userDto.Email);

            if (existingUser != null)
            {
                existingUser.Email = userDto.Email;
                if(userDto.UserName == null)
                {
                    throw new InvalidDataException($"User name is required for user: {userDto.Email}");
                }
                existingUser.UserName = userDto.UserName;
                existingUser.PasswordHashOrKey = passwordDto?.PasswordHashOrKey ?? existingUser.PasswordHashOrKey;
                existingUser.LoginMethod = passwordDto?.LoginMethod ?? existingUser.LoginMethod;
                _context.Users.Update(existingUser);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            else
            {
                if (passwordDto?.PasswordHashOrKey == null)
                {
                    throw new InvalidDataException($"Password hash or key is required for new user: {userDto.Email}");
                }
                var newUser = _mapper.Map<User>(userDto);
                newUser.PasswordHashOrKey = passwordDto.PasswordHashOrKey;
                newUser.LoginMethod = passwordDto.LoginMethod;
                newUser.Id = Guid.NewGuid();
                newUser.CreatedAt = DateTime.UtcNow;
                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                await SubscribeUserToDefaultCanvasesAsync(newUser.Id, passwordDto.LoginMethod == LoginMethod.Guest);
            }

            var userInDb = await GetByEmailAsync(userDto.Email);
            if (userInDb == null)
            {
                throw new InvalidOperationException($"Could not find user after adding/updating: {userDto.Email}");
            }
            return await GetByEmailAsync(userDto.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AddOrUpdateUserAsync for user: {Email}", userDto.Email);
            await transaction.RollbackAsync();
            return null;
        }
    }

    public async Task<UserDto?> CreateGuestUserAsync()
    {
        const int maxAttempts = 32;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var userName = $"{GuestUserHelper.GuestUserNamePrefix}{RandomNumberGenerator.GetInt32(0, 100_000_000):D8}";
            var email = GuestUserHelper.BuildGuestEmail(userName);

            if (await _context.Users.AsNoTracking().AnyAsync(user => user.UserName == userName || user.Email == email))
            {
                continue;
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    UserName = userName,
                    Email = email,
                    LoginMethod = LoginMethod.Guest,
                    PasswordHashOrKey = SecurityHelper.HashPassword(Guid.NewGuid().ToString("N")),
                    CreatedAt = DateTime.UtcNow,
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await SubscribeUserToDefaultCanvasesAsync(newUser.Id, isGuest: true);
                return _mapper.Map<UserDto>(newUser);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogDebug(ex, "Guest user creation attempt {Attempt} collided on username/email {UserName}", attempt, userName);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating guest user during attempt {Attempt}", attempt);
                return null;
            }
        }

        _logger.LogError("Failed to create a unique guest user after {AttemptCount} attempts.", maxAttempts);
        return null;
    }

    public async Task<int> DeleteExpiredGuestUsersAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var guestUserIds = await _context.Users
            .AsNoTracking()
            .Where(user => user.LoginMethod == LoginMethod.Guest && user.CreatedAt <= cutoffUtc)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        if (guestUserIds.Count == 0)
        {
            return 0;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var guestCanvasIds = await _context.Canvases
                .Where(canvas => guestUserIds.Contains(canvas.CreatorId))
                .Select(canvas => canvas.Id)
                .ToListAsync(cancellationToken);

            if (guestCanvasIds.Count > 0)
            {
                await _context.Canvases
                    .Where(canvas => guestCanvasIds.Contains(canvas.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var guestOwnedPixelIds = await _context.Pixels
                .Where(pixel => pixel.OwnerId.HasValue && guestUserIds.Contains(pixel.OwnerId.Value))
                .Select(pixel => pixel.Id)
                .ToListAsync(cancellationToken);

            await _context.PixelChangedEvents
                .Where(pixelEvent =>
                    guestUserIds.Contains(pixelEvent.OwnerUserId)
                    || (pixelEvent.OldOwnerUserId.HasValue && guestUserIds.Contains(pixelEvent.OldOwnerUserId.Value))
                    || guestOwnedPixelIds.Contains(pixelEvent.PixelId))
                .ExecuteDeleteAsync(cancellationToken);

            if (guestOwnedPixelIds.Count > 0)
            {
                await _context.Pixels
                    .Where(pixel => guestOwnedPixelIds.Contains(pixel.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var deletedUserCount = await _context.Users
                .Where(user => guestUserIds.Contains(user.Id))
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return deletedUserCount;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error deleting expired guest users older than {CutoffUtc}", cutoffUtc);
            return 0;
        }
    }

    public async Task<UserDto?> DeleteUserAsync(UserDto userDto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userDto.Id);
        if (user == null)
            return null;

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return _mapper.Map<UserDto>(user);
    }

    public Task<UserDto?> TryLogin(UserDto userDto, UserPaswordDto userPaswordDto)
    {
        return _context.Users
        .AsNoTracking()
        .Where(u => u.Email == userDto.Email && u.PasswordHashOrKey == userPaswordDto.PasswordHashOrKey && u.LoginMethod == userPaswordDto.LoginMethod)
        .ProjectTo<UserDto>(_mapper.ConfigurationProvider)
        .FirstOrDefaultAsync();
    }

    private async Task SubscribeUserToDefaultCanvasesAsync(Guid userId, bool isGuest)
    {
        var mainCanvas = await _context.Canvases
            .AsNoTracking()
            .FirstOrDefaultAsync(canvas => canvas.Name == _defaultsConfig.DefaultCanvasName);

        if (mainCanvas == null)
        {
            throw new InvalidOperationException($"Main canvas with name '{_defaultsConfig.DefaultCanvasName}' not found.");
        }

        await _subscriptionRepository.Subscribe(userId, mainCanvas.Id, null);

        foreach (var secondaryName in _defaultsConfig.SecondaryDefaultCanvasNames)
        {
            var secondaryCanvas = await _context.Canvases
                .AsNoTracking()
                .FirstOrDefaultAsync(canvas => canvas.Name == secondaryName);
            if (secondaryCanvas != null)
            {
                try
                {
                    await _subscriptionRepository.Subscribe(userId, secondaryCanvas.Id, null);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not auto-subscribe new user {UserId} to secondary canvas '{CanvasName}'", userId, secondaryName);
                }
            }
            else
            {
                _logger.LogDebug("Secondary canvas '{CanvasName}' not found, skipping auto-subscription for user {UserId}", secondaryName, userId);
            }
        }
    }
}

