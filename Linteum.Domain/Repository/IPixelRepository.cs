using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface IPixelRepository
{
    Task<IEnumerable<PixelDto>> GetByCanvasIdAsync(Guid canvasId);

    /// <summary>
    /// Streams pixels for a canvas as an async sequence, projecting only the fields needed for
    /// rendering (<see cref="PixelDto.X"/>, <see cref="PixelDto.Y"/>, <see cref="PixelDto.ColorId"/>).
    /// Unlike <see cref="GetByCanvasIdAsync"/>, this never buffers the whole canvas in memory, so it
    /// is the safe path for rendering/export of large canvases (P-PERF-01).
    /// </summary>
    IAsyncEnumerable<PixelDto> StreamPixelsForCanvasAsync(Guid canvasId);

    Task<IEnumerable<PixelDto>> GetByOwnerIdAsync(Guid ownerId);
    Task<PixelDto?> GetByPixelDto(PixelDto pixelDto);
    Task<PixelDto?> TryChangePixelAsync(Guid ownerId, PixelDto pixel);
    Task<PixelBatchChangeResultDto> TryChangePixelsBatchAsync(Guid ownerId, IReadOnlyCollection<PixelDto> pixels, bool useMasterOverride = false, bool suppressNotifications = false);
    Task<PixelBatchDeleteResultDto> TryDeletePixelsBatchAsync(Guid userId, IReadOnlyCollection<CoordinateDto> coordinates, Guid canvasId, bool useMasterOverride = false, bool suppressNotifications = false);
    Task<NormalModeQuotaDto> GetNormalModeQuotaAsync(Guid ownerId, Guid canvasId);
}

