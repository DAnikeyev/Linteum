using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace Linteum.Infrastructure;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly IBalanceChangedEventRepository _balanceChangedEventRepository;
    private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    public SubscriptionRepository(AppDbContext context, IMapper mapper, IBalanceChangedEventRepository balanceChangedEventRepository)
    {
        _context = context;
        _mapper = mapper;
        _balanceChangedEventRepository = balanceChangedEventRepository;
    }

    public async Task<IEnumerable<SubscriptionDto>> GetByUserIdAsync(Guid userId)
    {
        return await _context.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ProjectTo<SubscriptionDto>(_mapper.ConfigurationProvider)
            .ToListAsync();
    }

    public async Task<IEnumerable<SubscriptionDto>> GetByCanvasIdAsync(Guid canvasId)
    {
        return await _context.Subscriptions
            .AsNoTracking()
            .Where(s => s.CanvasId == canvasId)
            .ProjectTo<SubscriptionDto>(_mapper.ConfigurationProvider)
            .ToListAsync();
    }

    public async Task<SubscriptionDto?> Subscribe(Guid userId, Guid canvasId, string? passwordHash)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var existing = await _context.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId && s.CanvasId == canvasId);

            var canvas = await _context.Canvases
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == canvasId);
            if (canvas == null)
            {
                _logger.Warn("Canvas not found. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                throw new InvalidOperationException("Canvas not found.");
            }
            if (canvas.PasswordHash != passwordHash)
            {
                _logger.Warn("Invalid password for the canvas. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                throw new InvalidOperationException("Invalid password for the canvas.");
            }
            if (existing != null)
            {
                _logger.Info("User already subscribed. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                return _mapper.Map<SubscriptionDto>(existing);
            }

            var subscription = new Subscription
            {
                UserId = userId,
                CanvasId = canvasId,
            };

            _context.Subscriptions.Add(subscription);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            await _balanceChangedEventRepository.TryChangeBalanceAsync(userId, canvasId, 1, BalanceChangedReason.Subscription);
            _logger.Info("User subscribed successfully. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
            return _mapper.Map<SubscriptionDto>(subscription);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.Error(ex, "Error subscribing user. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
            return null;
        }
    }

    public async Task<SubscriptionDto?> Unsubscribe(Guid userId, Guid canvasId)
    {
        var sub = _context.Subscriptions.Where(x => x.UserId == userId && x.CanvasId == canvasId).FirstOrDefault();

        if (sub is null)
        {
            return null;
        }

        var balance = await _balanceChangedEventRepository.GetByUserAndCanvasIdAsync(userId, canvasId);
        // ReSharper disable once PossibleMultipleEnumeration
        if (balance.Any())
        {
            // ReSharper disable once PossibleMultipleEnumeration
            var delta = -balance.Last().NewBalance;
            await _balanceChangedEventRepository.TryChangeBalanceAsync(userId, canvasId, delta, BalanceChangedReason.Unsubscription);
        }

        _context.Subscriptions.Remove(sub);
        await _context.SaveChangesAsync();
        return _mapper.Map<SubscriptionDto>(sub);
    }
}

