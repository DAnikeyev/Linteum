using Linteum.Api.Hubs;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR;

namespace Linteum.Api.Services;

public class SignalRPixelNotifier : IPixelNotifier
{
    private readonly IHubContext<CanvasHub> _hubContext;
    private readonly IConnectionTracker _tracker;
    private readonly ILogger<SignalRPixelNotifier> _logger;

    public SignalRPixelNotifier(IHubContext<CanvasHub> hubContext, IConnectionTracker tracker, ILogger<SignalRPixelNotifier> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task NotifyPixelChanged(string canvasName, PixelDto pixel)
    {
        await NotifyPixelsChanged(canvasName, [pixel]);
    }

    public async Task NotifyPixelsChanged(string canvasName, IReadOnlyCollection<PixelDto> pixels)
    {
        if (pixels.Count == 0)
        {
            return;
        }

        var count = _tracker.GetGroupCount(canvasName);
        _logger.LogDebug("Notifying {Count} clients in group {CanvasName} about {PixelCount} pixel updates", count, canvasName, pixels.Count);

        if (pixels.Count == 1)
        {
            var pixel = pixels.First();
            await _hubContext.Clients.Group(canvasName).SendAsync(CanvasHub.ReceivePixelUpdateEventName, pixel);
            return;
        }

        await _hubContext.Clients.Group(canvasName).SendAsync(CanvasHub.ReceivePixelBatchUpdateEventName, pixels);
    }

    public async Task NotifyPixelsDeleted(string canvasName, IReadOnlyCollection<CoordinateDto> coordinates)
    {
        if (coordinates.Count == 0)
        {
            return;
        }

        var count = _tracker.GetGroupCount(canvasName);
        _logger.LogDebug("Notifying {Count} clients in group {CanvasName} about {PixelCount} pixel deletions", count, canvasName, coordinates.Count);

        await _hubContext.Clients.Group(canvasName).SendAsync(CanvasHub.PixelsDeletedEventName, coordinates);
    }

    public async Task NotifyConfirmedPixelsChanged(string canvasName, ConfirmedPixelPlaybackBatchDto playbackBatch)
    {
        if (playbackBatch.Pixels.Count == 0)
        {
            return;
        }

        var count = _tracker.GetGroupCount(canvasName);
        _logger.LogDebug("Broadcasting confirmed paint playback to {Count} clients in group {CanvasName}. PixelCount={PixelCount}, StrokeId={StrokeId}, ChunkSequence={ChunkSequence}", count, canvasName, playbackBatch.Pixels.Count, playbackBatch.StrokeId, playbackBatch.ChunkSequence);

        await _hubContext.Clients.Group(canvasName).SendAsync(CanvasHub.ReceiveConfirmedPixelPlaybackBatchEventName, playbackBatch);
    }

    public async Task NotifyConfirmedPixelsDeleted(string canvasName, ConfirmedPixelDeletionPlaybackBatchDto playbackBatch)
    {
        if (playbackBatch.Coordinates.Count == 0)
        {
            return;
        }

        var count = _tracker.GetGroupCount(canvasName);
        _logger.LogDebug("Broadcasting confirmed erase playback to {Count} clients in group {CanvasName}. PixelCount={PixelCount}, StrokeId={StrokeId}, ChunkSequence={ChunkSequence}", count, canvasName, playbackBatch.Coordinates.Count, playbackBatch.StrokeId, playbackBatch.ChunkSequence);

        await _hubContext.Clients.Group(canvasName).SendAsync(CanvasHub.ReceiveConfirmedPixelDeletionPlaybackBatchEventName, playbackBatch);
    }
}
