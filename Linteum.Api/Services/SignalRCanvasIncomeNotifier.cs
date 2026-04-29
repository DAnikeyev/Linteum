using Linteum.Api.Hubs;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR;

namespace Linteum.Api.Services;

public class SignalRCanvasIncomeNotifier : ICanvasIncomeNotifier
{
    private readonly IHubContext<CanvasHub> _hubContext;
    private readonly IConnectionTracker _tracker;
    private readonly ILogger<SignalRCanvasIncomeNotifier> _logger;

    public SignalRCanvasIncomeNotifier(
        IHubContext<CanvasHub> hubContext,
        IConnectionTracker tracker,
        ILogger<SignalRCanvasIncomeNotifier> logger)
    {
        _hubContext = hubContext;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task NotifyCanvasIncomeAsync(string canvasName, IReadOnlyCollection<CanvasIncomeUpdateDto> updates, CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
        {
            return;
        }

        var onlineCount = _tracker.GetGroupCount(canvasName);
        _logger.LogDebug(
            "Broadcasting {UpdateCount} hourly income updates to canvas group {CanvasName}. Online connections: {OnlineCount}",
            updates.Count,
            canvasName,
            onlineCount);

        await _hubContext.Clients.Group(canvasName).SendAsync("ReceiveCanvasIncomeUpdates", updates, cancellationToken);
    }
}
