using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface ICanvasRepository
{
    public Task<IEnumerable<CanvasDto>> GetByUserIdAsync(Guid userId);
    public Task<CanvasDto?> GetByNameAsync(string name);
    public Task<IEnumerable<CanvasDto>> GetAllAsync(bool includePrivates = false);
    public Task<bool> TryDeleteCanvasByName(string name);
    public Task<bool> CheckPassword(CanvasDto canvas, string? passwordHash);
    public Task<CanvasDto?> TryAddCanvas(CanvasDto canvas, string? passwordHash);
}