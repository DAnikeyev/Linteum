using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface IPixelRepository
{
    Task<IEnumerable<PixelDto>> GetByCanvasIdAsync(Guid canvasId);
    Task<IEnumerable<PixelDto>> GetByOwnerIdAsync(Guid ownerId);
    Task<PixelDto?> GetByPixelDto(PixelDto pixelDto);
    Task<PixelDto?> TryChangePixelAsync(Guid ownerId, PixelDto pixel);
    Task<PixelBatchChangeResultDto> TryChangePixelsBatchAsync(Guid ownerId, IReadOnlyCollection<PixelDto> pixels, bool useMasterOverride = false, bool suppressNotifications = false);
    Task<PixelBatchDeleteResultDto> TryDeletePixelsBatchAsync(Guid userId, IReadOnlyCollection<CoordinateDto> coordinates, Guid canvasId, bool useMasterOverride = false, bool suppressNotifications = false);
    Task<NormalModeQuotaDto> GetNormalModeQuotaAsync(Guid ownerId, Guid canvasId);
}

