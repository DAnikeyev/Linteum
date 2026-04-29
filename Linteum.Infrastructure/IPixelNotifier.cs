using Linteum.Shared.DTO;

namespace Linteum.Infrastructure;

public interface IPixelNotifier
{
    Task NotifyPixelChanged(string canvasName, PixelDto pixel);
    Task NotifyPixelsChanged(string canvasName, IReadOnlyCollection<PixelDto> pixels);
    Task NotifyPixelsDeleted(string canvasName, IReadOnlyCollection<CoordinateDto> coordinates);
    Task NotifyConfirmedPixelsChanged(string canvasName, ConfirmedPixelPlaybackBatchDto playbackBatch);
    Task NotifyConfirmedPixelsDeleted(string canvasName, ConfirmedPixelDeletionPlaybackBatchDto playbackBatch);
}