using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface IBalanceChangedEventRepository
{
    public Task<IEnumerable<BalanceChangedEventDto>> GetByUserIdAsync(Guid userId);
    public Task<IEnumerable<BalanceChangedEventDto>> GetByUserAndCanvasIdAsync(Guid userId, Guid CanvasId);
    public Task<BalanceChangedEventDto?> TryChangeBalanceAsync(Guid userId, Guid canvasId, long delta, BalanceChangedReason reason);

    /// <summary>
    /// Applies a guarded balance change using the caller's ambient transaction and WITHOUT acquiring the
    /// canvas write-coordinator lock. Reads the authoritative latest balance, rejects a result that would
    /// go negative (returns null), otherwise appends a single <see cref="BalanceChangedEvent"/> and saves.
    /// </summary>
    /// <remarks>
    /// Contract: the caller is responsible for both the transaction and serialization. Either the caller
    /// already holds the canvas write-coordinator lock for <paramref name="canvasId"/> (see
    /// <see cref="TryChangeBalanceAsync"/>, <c>SubscriptionRepository.Subscribe/Unsubscribe</c>, and the
    /// economy pixel path), or there is no concurrent writer for this (userId, canvasId) pair (e.g. a
    /// brand-new user). Never acquire the canvas lock from inside a path that already holds it —
    /// <see cref="CanvasWriteCoordinator"/> uses non-reentrant semaphores.
    /// </remarks>
    public Task<BalanceChangedEventDto?> TryChangeBalanceCoreAsync(Guid userId, Guid canvasId, long delta, BalanceChangedReason reason);
}