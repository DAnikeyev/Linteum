using AutoMapper;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Linteum.Infrastructure;

public class PixelRepository : IPixelRepository
{
    private static int? DefaultColorId;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly ILogger<PixelRepository> _logger;
    private readonly IPixelNotifier _notifier;
    private readonly IColorRepository _colorRepository;

    public PixelRepository(AppDbContext context, IMapper mapper, ILogger<PixelRepository> logger, IPixelNotifier notifier, IColorRepository colorRepository)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
        _notifier = notifier;
        _colorRepository = colorRepository;
    }

    private async Task<int?> GetDefaultColorIdAsync()
    {
        if (DefaultColorId != null) return DefaultColorId;

        var defaultColor = await _colorRepository.GetDefautColor();
        if (defaultColor != null)
        {
            DefaultColorId = defaultColor.Id;
        }
        return DefaultColorId;
    }

    public async Task<IEnumerable<PixelDto>> GetByCanvasIdAsync(Guid canvasId)
    {
        var pixels = await _context.Pixels
            .Where(p => p.CanvasId == canvasId)
            .Select(p => _mapper.Map<PixelDto>(p))
            .ToListAsync();
        return pixels;
    }

    public async Task<IEnumerable<PixelDto>> GetByOwnerIdAsync(Guid ownerId)
    {
        var pixels = await _context.Pixels
            .Where(p => p.OwnerId == ownerId)
            .Select(p => _mapper.Map<PixelDto>(p))
            .ToListAsync();
        return pixels;
    }

    public async Task<PixelDto?> GetByPixelDto(PixelDto pixelDto)
    {
        var pixel = await _context.Pixels
            .Where(p => p.CanvasId == pixelDto.CanvasId && p.X == pixelDto.X && p.Y == pixelDto.Y)
            .Select(p => _mapper.Map<PixelDto>(p))
            .FirstOrDefaultAsync();
        return _mapper.Map<PixelDto>(pixel);
    }

    public async Task<PixelDto?> TryChangePixelAsync(Guid ownerId, PixelDto pixel)
    {
        var canvas = await _context.Canvases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == pixel.CanvasId);
        if (canvas == null)
        {
            _logger.LogDebug("Canvas with ID {CanvasId} not found.", pixel.CanvasId);
            return null;
        }
        if (canvas.Height < pixel.Y || canvas.Width < pixel.X || pixel.X < 0 || pixel.Y < 0)
        {
            _logger.LogDebug("Pixel coordinates ({X}, {Y}) are out of bounds for canvas {CanvasName} (Width: {Width}, Height: {Height}).", pixel.X, pixel.Y, canvas.Name, canvas.Width, canvas.Height);
            return null;
        }

        var result = canvas.CanvasMode switch
        {
            CanvasMode.Sandbox => await TryChangePixelSandbox(pixel, ownerId),
            CanvasMode.Economy => await TryChangePixelEconomy(pixel, ownerId),
            _ => null,
        };

        if (canvas.CanvasMode is not CanvasMode.Sandbox and not CanvasMode.Economy)
        {
                _logger.LogDebug("Unknown canvas mode: {CanvasMode}", canvas.CanvasMode);
                return null;
        }

        if (result != null)
        {
            try
            {
                _logger.LogDebug("Signaling pixel change to clients. Canvas: {CanvasName}, Pixel: ({X}, {Y}), ColorId: {ColorId}", canvas.Name, result.X, result.Y, result.ColorId);
                await _notifier.NotifyPixelChanged(canvas.Name, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast pixel update");
            }
        }

        return result;
    }

    private async Task<PixelDto?> TryChangePixelSandbox(PixelDto pixel, Guid ownerId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var existingPixel = await _context.Pixels
                    .FirstOrDefaultAsync(p => p.CanvasId == pixel.CanvasId && p.X == pixel.X && p.Y == pixel.Y);

                var oldOwnerId = existingPixel?.OwnerId;
                int oldColorId;
                Pixel result;

                if (existingPixel == null)
                {
                    var defaultColorId = await GetDefaultColorIdAsync();
                    if (defaultColorId == null)
                    {
                        _logger.LogDebug("Default color not found for pixel ({X}, {Y}) on canvas {CanvasId}", pixel.X, pixel.Y, pixel.CanvasId);
                        return null;
                    }

                    result = new Pixel
                    {
                        Id = Guid.NewGuid(),
                        CanvasId = pixel.CanvasId,
                        X = pixel.X,
                        Y = pixel.Y,
                        ColorId = pixel.ColorId,
                        OwnerId = ownerId,
                        Price = 0,
                    };

                    oldColorId = defaultColorId.Value;
                    await _context.Pixels.AddAsync(result);
                }
                else
                {
                    oldColorId = existingPixel.ColorId;
                    existingPixel.ColorId = pixel.ColorId;
                    existingPixel.OwnerId = ownerId;
                    result = existingPixel;
                }

                var pixelChangedEvent = new PixelChangedEvent
                {
                    Id = Guid.NewGuid(),
                    PixelId = result.Id,
                    OldOwnerUserId = oldOwnerId,
                    OwnerUserId = ownerId,
                    OldColorId = oldColorId,
                    NewColorId = pixel.ColorId,
                    NewPrice = 0,
                    ChangedAt = DateTime.UtcNow,
                };

                await _context.PixelChangedEvents.AddAsync(pixelChangedEvent);
                await _context.SaveChangesAsync();

                _logger.LogDebug("Pixel changed successfully. PixelId={PixelId}, OwnerId={OwnerId}, CanvasId={CanvasId}", result.Id, ownerId, pixel.CanvasId);
                return _mapper.Map<PixelDto>(result);
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsPixelConflict(ex))
            {
                _context.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();
                _logger.LogError(ex, "Error changing pixel. OwnerId={OwnerId}, Pixel=({X},{Y})", ownerId, pixel.X, pixel.Y);
                return null;
            }
        }

        _logger.LogDebug("Pixel change conflicted repeatedly. OwnerId={OwnerId}, Pixel=({X},{Y}), CanvasId={CanvasId}", ownerId, pixel.X, pixel.Y, pixel.CanvasId);
        return null;
    }

    private static bool IsPixelConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
               && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private async Task<PixelDto?> TryChangePixelEconomy(PixelDto pixel, Guid ownerId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var existingPixel = await _context.Pixels
                .FirstOrDefaultAsync(p => p.CanvasId == pixel.CanvasId && pixel.X == p.X && pixel.Y == p.Y);
            if (existingPixel == null)
            {
                //Add pixel
                var addedPixel = new Pixel()
                {
                    Id = new Guid(),
                    CanvasId = pixel.CanvasId,
                    OwnerId = null,
                    ColorId = pixel.ColorId,
                    X = pixel.X,
                    Y = pixel.Y,
                    Price = 0,
                };
                _context.Pixels.Add(addedPixel);
                await _context.SaveChangesAsync();
                existingPixel = addedPixel;
            }

            var price = existingPixel.Price + 1;
            var balanceEvent = await _context.BalanceChangedEvents.AsNoTracking().Where(x => x.UserId == ownerId).OrderByDescending(x => x.ChangedAt)
                .FirstOrDefaultAsync();
            var paid = pixel.Price;
            if (balanceEvent == null)
            {
                throw new InvalidOperationException("No balance event found for the user.");
            }
            if (paid < price)
            {
                throw new InvalidOperationException("Insufficient payment to change the pixel.");
            }
            if (balanceEvent.NewBalance < paid)
            {
                throw new InvalidOperationException("Insufficient balance to change the pixel.");
            }

            var balanceUpdate = await TryChangeBalanceAsync(ownerId, pixel.CanvasId, -paid, BalanceChangedReason.PixelPayment);
            
            if (balanceUpdate == null)
            {
                throw new InvalidOperationException("Failed to update balance.");
            }
            
            var pixelChangedEvent = new PixelChangedEvent
            {
                Id = Guid.NewGuid(),
                PixelId = existingPixel.Id,
                OldOwnerUserId = existingPixel.OwnerId, 
                OwnerUserId= ownerId,
                OldColorId = existingPixel.ColorId,
                NewColorId = pixel.ColorId,
                NewPrice = paid,
                ChangedAt = DateTime.UtcNow,
            };
            existingPixel.Price = paid;
            existingPixel.OwnerId = ownerId;
            existingPixel.ColorId = pixel.ColorId;
            _context.Pixels.Update(existingPixel);
            await _context.PixelChangedEvents.AddAsync(pixelChangedEvent);
            await _context.SaveChangesAsync();
            _logger.LogDebug("Pixel changed successfully. PixelId={PixelId}, OwnerId={OwnerId}, CanvasId={CanvasId}, Price={Price}", existingPixel.Id, ownerId, pixel.CanvasId, paid);
            await transaction.CommitAsync();
            return _mapper.Map<PixelDto>(existingPixel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message, "Error changing pixel. OwnerId={OwnerId}, Pixel={Pixel}", ownerId, pixel);
            await transaction.RollbackAsync();
            return null;
        }
    }

    private async Task<BalanceChangedEvent?> TryChangeBalanceAsync(Guid userId, Guid canvasId, long delta, BalanceChangedReason reason)
    {         
        var lastEntry = await _context.BalanceChangedEvents
            .Where(e => e.UserId == userId && e.CanvasId == canvasId)
            .OrderByDescending(e => e.ChangedAt)
            .FirstOrDefaultAsync();

        var newBalance = lastEntry?.NewBalance + delta ?? delta;
        if (newBalance < 0)
        {
            return null;
        }

        var newEvent = new BalanceChangedEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CanvasId = canvasId,
            ChangedAt = DateTime.UtcNow,
            NewBalance = newBalance,
            OldBalance = lastEntry?.NewBalance ?? 0,
            Reason = reason,
        };
        await _context.BalanceChangedEvents.AddAsync(newEvent);
        return newEvent;
    }
}
