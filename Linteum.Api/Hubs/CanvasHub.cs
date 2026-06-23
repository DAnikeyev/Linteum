using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR;

namespace Linteum.Api.Hubs;

public class CanvasHub : Hub
{
    public const string ReceivePixelUpdateEventName = "ReceivePixelUpdate";
    public const string ReceivePixelBatchUpdateEventName = "ReceivePixelBatchUpdate";
    public const string ReceiveConfirmedPixelPlaybackBatchEventName = "ReceiveConfirmedPixelPlaybackBatch";
    public const string ReceiveConfirmedPixelDeletionPlaybackBatchEventName = "ReceiveConfirmedPixelDeletionPlaybackBatch";
    public const string UpdateOnlineUsersEventName = "UpdateOnlineUsers";
    public const string ReceiveCanvasChatMessageEventName = "ReceiveCanvasChatMessage";
    public const string SessionExpiredEventName = "SessionExpired";
    public const string CanvasErasedEventName = "CanvasErased";
    public const string CanvasDeletedEventName = "CanvasDeleted";
    public const string CanvasMaintenanceProgressEventName = "CanvasMaintenanceProgress";
    public const string PixelsDeletedEventName = "PixelsDeleted";

    private readonly IConnectionTracker _tracker;
    private readonly SessionService _sessionService;
    private readonly RepositoryManager _repositoryManager;
    private readonly ICanvasChatBroadcaster _canvasChatBroadcaster;
    private readonly ICanvasEventBuffer _eventBuffer;
    private readonly ILogger<CanvasHub> _logger;

    public CanvasHub(IConnectionTracker tracker, SessionService sessionService, RepositoryManager repositoryManager, ICanvasChatBroadcaster canvasChatBroadcaster, ICanvasEventBuffer eventBuffer, ILogger<CanvasHub> logger)
    {
        _tracker = tracker;
        _sessionService = sessionService;
        _repositoryManager = repositoryManager;
        _canvasChatBroadcaster = canvasChatBroadcaster;
        _eventBuffer = eventBuffer;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userName = await GetUserName();
        _tracker.AddConnection(Context.ConnectionId, userName);
        _logger.LogInformation("User {UserName} connected with ConnectionId {ConnectionId}", userName ?? "Anonymous", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var groups = _tracker.GetConnectionGroups(Context.ConnectionId);
        _tracker.RemoveConnection(Context.ConnectionId);
        _logger.LogInformation("ConnectionId {ConnectionId} disconnected from groups: {Groups}. Exception: {Exception}",
            Context.ConnectionId, string.Join(", ", groups), exception?.Message ?? "None");
        foreach (var group in groups)
        {
            await BroadcastUpdateOnlineUsers(group);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Adds the caller to a canvas group and returns the canvas event buffer's current high-water
    /// sequence number. The high-water mark is read AFTER the group membership is established, so
    /// the caller can treat <c>(returnedSeq, now]</c> as exactly the window of events it might have
    /// missed while it was loading a snapshot / reconnecting: any event with sequence &lt;= the
    /// returned value either predates its join or was also delivered live, and any event with a
    /// higher sequence will arrive live. See <c>GetCanvasChanges</c> for the replay fetch.
    /// </summary>
    public async Task<long> JoinCanvasGroup(string canvasName)
    {
        _tracker.AddToGroup(Context.ConnectionId, canvasName);
        await Groups.AddToGroupAsync(Context.ConnectionId, canvasName);
        var highWaterSeq = _eventBuffer.GetHighWaterSequence(canvasName);
        _logger.LogInformation("ConnectionId {ConnectionId} joined canvas group {CanvasName} at event sequence {EventSequence}", Context.ConnectionId, canvasName, highWaterSeq);
        await BroadcastUpdateOnlineUsers(canvasName);
        return highWaterSeq;
    }

    /// <summary>
    /// Returns buffered pixel-change entries with sequence in <c>(afterSeq, upToSeq]</c> for
    /// <paramref name="canvasName"/>, in ascending sequence order. Used by clients to reconcile the
    /// gap between a canvas snapshot and their live subscription (P-RT-02 / load-gap). Only the
    /// caller's own connection is gated on group membership; the session auth on the hub connection
    /// still applies.
    /// </summary>
    public Task<IReadOnlyList<CanvasChangeEntryDto>> GetCanvasChanges(string canvasName, long afterSeq, long upToSeq)
    {
        var joinedGroups = _tracker.GetConnectionGroups(Context.ConnectionId);
        if (!joinedGroups.Contains(canvasName, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<CanvasChangeEntryDto>>(Array.Empty<CanvasChangeEntryDto>());
        }

        var entries = _eventBuffer.GetRange(canvasName, afterSeq, upToSeq, CanvasReconcileLimits.MaxEntriesPerResponse);
        return Task.FromResult<IReadOnlyList<CanvasChangeEntryDto>>(entries);
    }

    /// <summary>
    /// Returns the most recent buffered entries for <paramref name="canvasName"/> (ascending by
    /// sequence). Used by the load-gap reconcile, which has no precise sequence anchor (the
    /// snapshot was rendered moments ago) and simply re-applies the freshest events idempotently.
    /// </summary>
    public Task<IReadOnlyList<CanvasChangeEntryDto>> GetRecentCanvasChanges(string canvasName, int max)
    {
        var joinedGroups = _tracker.GetConnectionGroups(Context.ConnectionId);
        if (!joinedGroups.Contains(canvasName, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<CanvasChangeEntryDto>>(Array.Empty<CanvasChangeEntryDto>());
        }

        var entries = _eventBuffer.GetRecent(canvasName, max > 0 ? max : CanvasReconcileLimits.MaxEntriesPerResponse);
        return Task.FromResult<IReadOnlyList<CanvasChangeEntryDto>>(entries);
    }

    public async Task LeaveCanvasGroup(string canvasName)
    {
        _tracker.RemoveFromGroup(Context.ConnectionId, canvasName);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, canvasName);
        _logger.LogInformation("ConnectionId {ConnectionId} left canvas group {CanvasName}", Context.ConnectionId, canvasName);
        await BroadcastUpdateOnlineUsers(canvasName);
    }

    public async Task SendCanvasChatMessage(string canvasName, string message)
    {
        if (string.IsNullOrWhiteSpace(canvasName))
        {
            throw new HubException("Canvas name is required.");
        }

        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new HubException("Message is required.");
        }

        if (normalizedMessage.Length > SendCanvasChatMessageRequestDto.MaxMessageLength)
        {
            throw new HubException($"Message must be {SendCanvasChatMessageRequestDto.MaxMessageLength} characters or less.");
        }

        var joinedGroups = _tracker.GetConnectionGroups(Context.ConnectionId);
        if (!joinedGroups.Contains(canvasName, StringComparer.OrdinalIgnoreCase))
        {
            throw new HubException("Join the canvas lobby before sending chat messages.");
        }

        var userName = await GetUserName() ?? "Anonymous";
        await _canvasChatBroadcaster.BroadcastAsync(
            canvasName,
            new CanvasChatMessageDto
            {
                CanvasName = canvasName,
                UserName = userName,
                Message = normalizedMessage,
                SentAtUtc = DateTime.UtcNow,
            },
            Context.ConnectionAborted);
    }

    private async Task BroadcastUpdateOnlineUsers(string groupName)
    {
        var users = _tracker.GetGroupUsers(groupName).ToList();
        _logger.LogDebug("Broadcasting online users update to group {GroupName}. User count: {UserCount}", groupName, users.Count);
        await Clients.Group(groupName).SendAsync(UpdateOnlineUsersEventName, users);
    }

    private async Task<string?> GetUserName()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null) return null;

        var sessionIdString = httpContext.Request.Query["access_token"].ToString();
        if (string.IsNullOrEmpty(sessionIdString))
        {
            sessionIdString = httpContext.Request.Headers[CustomHeaders.SessionId].ToString();
        }

        if (Guid.TryParse(sessionIdString, out var sessionId))
        {
            var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
            if (userId.HasValue)
            {
                var user = await _repositoryManager.UserRepository.GetByIdAsync(userId.Value);
                return user?.UserName;
            }
        }

        return null;
    }

}
