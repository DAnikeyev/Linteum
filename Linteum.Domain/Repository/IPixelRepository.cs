using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface IPixelRepository
{
    Task<IEnumerable<PixelDto>> GetByCanvasIdAsync(Guid canvasId);
    Task<IEnumerable<PixelDto>> GetByOwnerIdAsync(Guid ownerId);
    Task<PixelDto?> GetByPixelDto(PixelDto pixelDto);
    Task<PixelDto?> TryChangePixelAsync(Guid ownerId, PixelDto pixel);
}

