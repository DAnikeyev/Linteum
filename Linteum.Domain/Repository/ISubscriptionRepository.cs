using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface ISubscriptionRepository
{
    Task<IEnumerable<SubscriptionDto>> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<SubscriptionDto>> GetByCanvasIdAsync(Guid canvasId);

    /// <summary>
    /// Lightweight existence check used for canvas-access authorization (viewing a protected
    /// canvas, painting, deleting). Cheaper than <see cref="GetByUserIdAsync"/> because it does
    /// not materialize the subscription list.
    /// </summary>
    Task<bool> IsSubscribedAsync(Guid userId, Guid canvasId);
    Task<SubscriptionDto?> Subscribe(Guid userId, Guid canvasId, string? password = null);

    Task<SubscriptionDto?> Unsubscribe(Guid userId, Guid canvasId);

    /// <summary>
    /// Bulk-subscribes every user in <paramref name="userIds"/> to a canvas in a single transaction,
    /// skipping users already subscribed and crediting each new subscriber +1 balance. This is the
    /// batched equivalent of <see cref="Subscribe"/> for public canvases and collapses what would
    /// otherwise be one transaction + lock acquisition per user (P-PERF-04). Intended for seeding;
    /// the caller is responsible for ensuring the canvas accepts a null password (public canvas).
    /// </summary>
    /// <returns>The number of users newly subscribed.</returns>
    Task<int> SubscribeAllAsync(Guid canvasId, IReadOnlyCollection<Guid> userIds);
}

