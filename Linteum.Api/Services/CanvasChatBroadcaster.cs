using Linteum.Api.Hubs;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR;

namespace Linteum.Api.Services;

public interface ICanvasChatBroadcaster
{
    Task BroadcastAsync(string canvasName, CanvasChatMessageDto message, CancellationToken cancellationToken = default);
}

public class CanvasChatBroadcaster : ICanvasChatBroadcaster
{
    private readonly IHubContext<CanvasHub> _hubContext;
    private readonly IConnectionTracker _tracker;
    private readonly ILogger<CanvasChatBroadcaster> _logger;

    public CanvasChatBroadcaster(
        IHubContext<CanvasHub> hubContext,
        IConnectionTracker tracker,
        ILogger<CanvasChatBroadcaster> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task BroadcastAsync(string canvasName, CanvasChatMessageDto message, CancellationToken cancellationToken = default)
    {
        var onlineCount = _tracker.GetGroupCount(canvasName);
        _logger.LogInformation(
            "Broadcasting canvas chat message to {CanvasName}. Sender={UserName}, Length={MessageLength}, OnlineConnections={OnlineConnections}",
            canvasName,
            message.UserName,
            message.Message.Length,
            onlineCount);

        await _hubContext.Clients.Group(canvasName)
            .SendAsync(CanvasHub.ReceiveCanvasChatMessageEventName, message, cancellationToken);
    }
}

