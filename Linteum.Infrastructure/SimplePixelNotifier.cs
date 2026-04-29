using Linteum.Shared.DTO;

namespace Linteum.Infrastructure;

public class SimplePixelNotifier : IPixelNotifier
{
    public Task NotifyPixelChanged(string canvasName, PixelDto pixel)
    {
        Console.WriteLine($"Notifying pixel changed from {canvasName} to {pixel}");
        return Task.CompletedTask;
    }

    public Task NotifyPixelsChanged(string canvasName, IReadOnlyCollection<PixelDto> pixels)
    {
        Console.WriteLine($"Notifying {pixels.Count} pixel changes from {canvasName}");
        return Task.CompletedTask;
    }

    public Task NotifyPixelsDeleted(string canvasName, IReadOnlyCollection<CoordinateDto> coordinates)
    {
        Console.WriteLine($"Notifying {coordinates.Count} pixel deletions from {canvasName}");
        return Task.CompletedTask;
    }

    public Task NotifyConfirmedPixelsChanged(string canvasName, ConfirmedPixelPlaybackBatchDto playbackBatch)
    {
        Console.WriteLine($"Notifying confirmed playback of {playbackBatch.Pixels.Count} pixels from {canvasName}");
        return Task.CompletedTask;
    }

    public Task NotifyConfirmedPixelsDeleted(string canvasName, ConfirmedPixelDeletionPlaybackBatchDto playbackBatch)
    {
        Console.WriteLine($"Notifying confirmed playback of {playbackBatch.Coordinates.Count} deletions from {canvasName}");
        return Task.CompletedTask;
    }
}