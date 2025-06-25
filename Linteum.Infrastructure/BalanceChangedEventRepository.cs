using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Shared.DTO;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace Linteum.Infrastructure;

public class BalanceChangedEventRepository : IBalanceChangedEventRepository
{
    private IMapper _mapper;
    private AppDbContext _context;
    private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    
    public BalanceChangedEventRepository(AppDbContext context, IMapper mapper)
    {
        _mapper = mapper;
        _context = context;
    }
    
    public async Task<IEnumerable<BalanceChangedEventDto>> GetByUserIdAsync(Guid userId)
    {
        return await _context.BalanceChangedEvents.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(x => x.ChangedAt)
            .ProjectTo<BalanceChangedEventDto>(_mapper.ConfigurationProvider)
            .ToListAsync()
            .ContinueWith(t => t.Result.AsEnumerable());
    }

    public async Task<IEnumerable<BalanceChangedEventDto>> GetByUserAndCanvasIdAsync(Guid userId, Guid CanvasId)
    {
        return await _context.BalanceChangedEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.CanvasId == CanvasId)
            .OrderByDescending(x => x.ChangedAt)
            .ProjectTo<BalanceChangedEventDto>(_mapper.ConfigurationProvider)
            .ToListAsync()
            .ContinueWith(t => t.Result.AsEnumerable());
    }

    public async Task<BalanceChangedEventDto?> TryChangeBalanceAsync(Guid userId, Guid canvasId, long delta, BalanceChangedReason reason)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var lastEntry = await _context.BalanceChangedEvents
                .Where(e => e.UserId == userId && e.CanvasId == canvasId)
                .OrderByDescending(e => e.ChangedAt)
                .FirstOrDefaultAsync();

            var newBalance = lastEntry?.NewBalance + delta ?? delta;
            if (newBalance < 0)
            {
                await transaction.RollbackAsync();
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
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return _mapper.Map<BalanceChangedEventDto>(newEvent);
        }
        catch (DbUpdateException ex)
        {
            _logger.Error(ex, "Error updating balance for user {UserId} on canvas {CanvasId}", userId, canvasId);
            await transaction.RollbackAsync();
            return null;
        }
    }
}