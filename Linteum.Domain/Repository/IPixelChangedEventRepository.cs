using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface IPixelChangedEventRepository
{
    Task<IEnumerable<PixelChangedEventDto>> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<PixelChangedEventDto>> GetByPixelIdAsync(Guid pixelId);
    Task<IEnumerable<PixelChangedEventDto>> GetByCanvasIdAsync(Guid canvasId, DateTime? startDate);
    Task<bool> AddPixelChangedEvent(PixelChangedEventDto pixelChangedEventDto);
}

