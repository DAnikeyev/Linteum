using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NLog;

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

    public async Task<UserDto?> AddOrUpdateUserAsync(UserDto userDto, UserPaswordDto? passwordDto = null)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var existingUser = (userDto.Id != Guid.Empty) ?
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
                if(passwordDto.PasswordHashOrKey == null)
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
                var mainCanvas = _context.Canvases.AsNoTracking().FirstOrDefault(x => x.Name == _defaultsConfig.DefaultCanvasName);
                if (mainCanvas == null)
                {
                    throw new InvalidOperationException($"Main canvas with name '{_defaultsConfig.DefaultCanvasName}' not found.");
                }

                await _subscriptionRepository.Subscribe(newUser.Id, mainCanvas.Id, null);
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
}

