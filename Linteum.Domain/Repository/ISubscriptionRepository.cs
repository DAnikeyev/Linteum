using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface ISubscriptionRepository
{
    Task<IEnumerable<SubscriptionDto>> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<SubscriptionDto>> GetByCanvasIdAsync(Guid canvasId);
    Task<SubscriptionDto?> Subscribe(Guid userId, Guid canvasId, string? passwordHash = null);
    
    Task<SubscriptionDto?> Unsubscribe(Guid userId, Guid canvasId);
}

