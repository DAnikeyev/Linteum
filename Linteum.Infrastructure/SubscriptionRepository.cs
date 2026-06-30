using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Linteum.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly IBalanceChangedEventRepository _balanceChangedEventRepository;
    private readonly ICanvasWriteCoordinator _canvasWriteCoordinator;
    private readonly ILogger<SubscriptionRepository> _logger;

    public SubscriptionRepository(AppDbContext context, IMapper mapper, IBalanceChangedEventRepository balanceChangedEventRepository, ICanvasWriteCoordinator canvasWriteCoordinator, ILogger<SubscriptionRepository> logger)
    {
        _context = context;
        _mapper = mapper;
        _balanceChangedEventRepository = balanceChangedEventRepository;
        _canvasWriteCoordinator = canvasWriteCoordinator;
        _logger = logger;
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

    public async Task<bool> IsSubscribedAsync(Guid userId, Guid canvasId)
    {
        return await _context.Subscriptions
            .AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.CanvasId == canvasId);
    }

    public async Task<SubscriptionDto?> Subscribe(Guid userId, Guid canvasId, string? password)
    {
        return await _canvasWriteCoordinator.ExecuteAsync(canvasId, async _ =>
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
                    _logger.LogDebug("Canvas not found. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                    throw new CanvasNotFoundException(canvasId);
                }

                if (!IsCanvasPasswordValid(canvas, password, out var needsRehash))
                {
                    _logger.LogDebug("Invalid password for the canvas. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                    throw new InvalidCanvasPasswordException(canvasId);
                }

                if (needsRehash)
                {
                    // Lazy migration of a legacy canvas-password hash (SHA-256 / plaintext) to PBKDF2.
                    await _context.Canvases
                        .Where(c => c.Id == canvasId)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.PasswordHash, SecurityHelper.HashPassword(password!)));
                    _logger.LogInformation("Migrated legacy canvas password hash to PBKDF2 for canvas {CanvasId}.", canvasId);
                }
                if (existing != null)
                {
                    _logger.LogDebug("User already subscribed. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                    throw new UserAlreadySubscribedException(userId, canvasId);
                }

                var subscription = new Subscription
                {
                    UserId = userId,
                    CanvasId = canvasId,
                };

                _context.Subscriptions.Add(subscription);
                await _context.SaveChangesAsync();

                // Credit the +1 balance inside the same transaction so a subscription can never exist
                // without its credit (P-CON-01). +1 cannot go negative, but the guard still applies.
                var balanceResult = await _balanceChangedEventRepository.TryChangeBalanceCoreAsync(userId, canvasId, 1, BalanceChangedReason.Subscription);
                if (balanceResult == null)
                {
                    throw new BalanceUpdateException(canvasId, userId);
                }

                await transaction.CommitAsync();
                _logger.LogDebug("User subscribed successfully. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                return _mapper.Map<SubscriptionDto>(subscription);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Expected domain outcomes (already-subscribed / balance rollback) are normal 400s for
                // the caller, not system errors — log them at Debug so they don't raise false ERROR
                // alerts in the ELK stack. Genuinely unexpected exceptions still log at Error.
                if (ex is UserAlreadySubscribedException or BalanceUpdateException)
                {
                    _logger.LogDebug(ex, "Subscribe rolled back (expected outcome). userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                }
                else
                {
                    _logger.LogError(ex, "Error subscribing user. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                }
                throw;
            }
        });
    }

    public async Task<SubscriptionDto?> Unsubscribe(Guid userId, Guid canvasId)
    {
        return await _canvasWriteCoordinator.ExecuteAsync(canvasId, async _ =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sub = await _context.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId && x.CanvasId == canvasId);

                if (sub is null)
                {
                    _logger.LogDebug("Subscription not found for unsubscription. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                    throw new CanvasNotFoundException(canvasId);
                }

                // Read the authoritative latest balance (a single row, not the Take(500) history) so the
                // delta zeroes the real current balance even for users with long histories (P-CON-01/P-PERF-03).
                var latestBalance = await _context.BalanceChangedEvents
                    .AsNoTracking()
                    .Where(e => e.UserId == userId && e.CanvasId == canvasId)
                    .OrderByDescending(e => e.ChangedAt)
                    .ThenByDescending(e => e.Id)
                    .Select(e => (long?)e.NewBalance)
                    .FirstOrDefaultAsync();

                if (latestBalance is { } currentBalance && currentBalance != 0)
                {
                    var balanceResult = await _balanceChangedEventRepository.TryChangeBalanceCoreAsync(userId, canvasId, -currentBalance, BalanceChangedReason.Unsubscription);
                    if (balanceResult is null)
                    {
                        _logger.LogDebug("Failed to adjust balance during unsubscription. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                        throw new BalanceUpdateException(canvasId, userId);
                    }
                }

                _context.Subscriptions.Remove(sub);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return _mapper.Map<SubscriptionDto>(sub);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Expected domain outcomes (canvas-not-found / balance rollback) are normal 4xxs for
                // the caller, not system errors — log them at Debug to avoid false ERROR alerts.
                if (ex is CanvasNotFoundException or BalanceUpdateException)
                {
                    _logger.LogDebug(ex, "Unsubscribe rolled back (expected outcome). userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                }
                else
                {
                    _logger.LogError(ex, "Error unsubscribing user. userId={UserId}, canvasId={CanvasId}", userId, canvasId);
                }
                throw;
            }
        });
    }

    public async Task<int> SubscribeAllAsync(Guid canvasId, IReadOnlyCollection<Guid> userIds)
    {
        if (userIds.Count == 0)
        {
            return 0;
        }

        return await _canvasWriteCoordinator.ExecuteAsync(canvasId, async _ =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var canvas = await _context.Canvases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == canvasId);
                if (canvas == null)
                {
                    _logger.LogDebug("Bulk subscribe skipped: canvas {CanvasId} not found.", canvasId);
                    await transaction.CommitAsync();
                    return 0;
                }

                var userIdArray = userIds as Guid[] ?? userIds.ToArray();

                // Skip users already subscribed to this canvas (idempotent re-runs of the seeder).
                var alreadySubscribed = await _context.Subscriptions
                    .AsNoTracking()
                    .Where(s => s.CanvasId == canvasId && userIdArray.Contains(s.UserId))
                    .Select(s => s.UserId)
                    .ToListAsync();

                var newUserIds = userIdArray.Except(alreadySubscribed).ToList();
                if (newUserIds.Count == 0)
                {
                    await transaction.CommitAsync();
                    return 0;
                }

                // Authoritative latest balance per candidate (default 0). Mirrors the single-row read
                // in TryChangeBalanceCoreAsync, projected for all candidates and resolved in memory so
                // the query translates trivially. For a freshly-seeded canvas this is ~0 rows.
                var candidateBalances = await _context.BalanceChangedEvents
                    .AsNoTracking()
                    .Where(e => e.CanvasId == canvasId && newUserIds.Contains(e.UserId))
                    .Select(e => new { e.UserId, e.ChangedAt, e.Id, e.NewBalance })
                    .ToListAsync();

                var balanceByUser = candidateBalances
                    .GroupBy(e => e.UserId)
                    .Select(g => g.OrderByDescending(e => e.ChangedAt).ThenByDescending(e => e.Id).First())
                    .ToDictionary(x => x.UserId, x => (long)x.NewBalance);

                var now = DateTime.UtcNow;
                var subscriptions = new List<Subscription>(newUserIds.Count);
                var balanceEvents = new List<BalanceChangedEvent>(newUserIds.Count);

                foreach (var userId in newUserIds)
                {
                    subscriptions.Add(new Subscription
                    {
                        UserId = userId,
                        CanvasId = canvasId,
                    });

                    var currentBalance = balanceByUser.TryGetValue(userId, out var latest) ? latest : 0L;
                    balanceEvents.Add(new BalanceChangedEvent
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        CanvasId = canvasId,
                        ChangedAt = now,
                        OldBalance = currentBalance,
                        NewBalance = currentBalance + 1,
                        Reason = BalanceChangedReason.Subscription,
                    });
                }

                await _context.Subscriptions.AddRangeAsync(subscriptions);
                await _context.BalanceChangedEvents.AddRangeAsync(balanceEvents);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogDebug("Bulk subscribed {Count} users to canvas {CanvasId}.", newUserIds.Count, canvasId);
                return newUserIds.Count;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error bulk-subscribing {UserCount} users to canvas {CanvasId}", userIds.Count, canvasId);
                throw;
            }
        });
    }

    /// <summary>
    /// Validates a plaintext canvas password against the stored hash in constant time
    /// (P-SEC-04). <paramref name="needsRehash"/> is true for legacy stored hashes that
    /// should be upgraded to the PBKDF2 scheme. Public canvases accept no password.
    /// </summary>
    private static bool IsCanvasPasswordValid(Canvas canvas, string? password, out bool needsRehash)
    {
        needsRehash = false;

        if (string.IsNullOrEmpty(canvas.PasswordHash))
        {
            return string.IsNullOrEmpty(password);
        }

        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var (valid, legacy) = SecurityHelper.VerifyPassword(password, canvas.PasswordHash);
        needsRehash = valid && legacy;
        return valid;
    }
}

