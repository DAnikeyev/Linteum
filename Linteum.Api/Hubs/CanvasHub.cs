using Linteum.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Linteum.Api.Hubs;

public class CanvasHub : Hub
{
    private readonly IConnectionTracker _tracker;

    public CanvasHub(IConnectionTracker tracker)
    {
        _tracker = tracker;
    }

    public override async Task OnConnectedAsync()
    {
        _tracker.AddConnection(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _tracker.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinCanvasGroup(string canvasName)
    {
        _tracker.AddToGroup(Context.ConnectionId, canvasName);
        await Groups.AddToGroupAsync(Context.ConnectionId, canvasName);
    }

    public async Task LeaveCanvasGroup(string canvasName)
    {
        _tracker.RemoveFromGroup(Context.ConnectionId, canvasName);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, canvasName);
    }
}