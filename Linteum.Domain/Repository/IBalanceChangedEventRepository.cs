using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface IBalanceChangedEventRepository
{
    public Task<IEnumerable<BalanceChangedEventDto>> GetByUserIdAsync(Guid userId);
    public Task<IEnumerable<BalanceChangedEventDto>> GetByUserAndCanvasIdAsync(Guid userId, Guid CanvasId);
    public Task<BalanceChangedEventDto?> TryChangeBalanceAsync(Guid userId, Guid canvasId, long delta, BalanceChangedReason reason);
}