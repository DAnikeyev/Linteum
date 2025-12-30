using AutoMapper;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NLog;
using ILogger = NLog.ILogger;

namespace Linteum.Infrastructure;

public class PixelRepository : IPixelRepository
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly IBalanceChangedEventRepository _balanceChangedEventRepository;
    private readonly ILogger<PixelRepository> _logger;

    public PixelRepository(AppDbContext context, IMapper mapper, IBalanceChangedEventRepository balanceChangedEventRepository, ILogger<PixelRepository> logger)
    {
        _context = context;
        _mapper = mapper;
        _balanceChangedEventRepository = balanceChangedEventRepository;
        _logger = logger;
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
            _logger.LogWarning($"Canvas with ID {pixel.CanvasId} not found.");
            return null;
        }
        if (canvas.Height < pixel.Y || canvas.Width < pixel.X || pixel.X < 0 || pixel.Y < 0)
        {
            _logger.LogWarning($"Pixel coordinates ({pixel.X}, {pixel.Y}) are out of bounds for canvas {canvas.Name} (Width: {canvas.Width}, Height: {canvas.Height}).");
            return null;
        }
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
            _logger.LogInformation($"Pixel changed successfully. PixelId={existingPixel.Id}, OwnerId={ownerId}, CanvasId={pixel.CanvasId}, Price={paid}");
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