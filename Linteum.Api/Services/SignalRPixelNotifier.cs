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
        var count = _tracker.GetGroupCount(canvasName);
        _logger.LogInformation("Notifying {Count} clients in group {CanvasName} about pixel update at ({X}, {Y})", count, canvasName, pixel.X, pixel.Y);
        await _hubContext.Clients.Group(canvasName).SendAsync("ReceivePixelUpdate", pixel.X, pixel.Y, pixel.ColorId);
    }
}
