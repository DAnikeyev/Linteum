using Linteum.Api.Hubs;
using Linteum.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace Linteum.Api.Services;

public class MinuteCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MinuteCleanupService> _logger;
    private readonly SessionService _sessionService;
    private readonly IConnectionTracker _tracker;
    private readonly IHubContext<CanvasHub> _hubContext;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public MinuteCleanupService(
        IServiceProvider serviceProvider,
        ILogger<MinuteCleanupService> logger,
        SessionService sessionService,
        IConnectionTracker tracker,
        IHubContext<CanvasHub> hubContext)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _sessionService = sessionService;
        _tracker = tracker;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Minute Cleanup Service is starting.");

        try
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var expiredSessions = _sessionService.CleanupExpiredSessions();
                    
                    if (expiredSessions.Any())
                    {
                        _logger.LogInformation("Cleaning up {Count} expired sessions.", expiredSessions.Count);
                        
                        using var scope = _serviceProvider.CreateScope();
                        var repositoryManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();

                        foreach (var session in expiredSessions)
                        {
                            if (stoppingToken.IsCancellationRequested)
                            {
                                break;
                            }

                            var user = await repositoryManager.UserRepository.GetByIdAsync(session.UserId);
                            if (user?.UserName == null) continue;

                            var connections = _tracker.GetUserConnections(user.UserName);
                            foreach (var connectionId in connections)
                            {
                                if (stoppingToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                await _hubContext.Clients.Client(connectionId)
                                    .SendAsync(CanvasHub.SessionExpiredEventName, cancellationToken: stoppingToken);
                                _logger.LogInformation(
                                    "Notified ConnectionId {ConnectionId} (User {UserName}) about session expiration.",
                                    connectionId,
                                    user.UserName);

                                var groups = _tracker.GetConnectionGroups(connectionId).ToList();
                                foreach (var group in groups)
                                {
                                    stoppingToken.ThrowIfCancellationRequested();

                                    _tracker.RemoveFromGroup(connectionId, group);
                                    await _hubContext.Groups.RemoveFromGroupAsync(connectionId, group, stoppingToken);
                                    _logger.LogInformation("Removed ConnectionId {ConnectionId} (User {UserName}) from group {GroupName} due to session expiration.", 
                                        connectionId, user.UserName, group);
                                    
                                    await BroadcastUpdateOnlineUsers(group);
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during the minute cleanup task.");
                }

                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger.LogInformation("Minute Cleanup Service is stopping.");
        }
    }

    private async Task BroadcastUpdateOnlineUsers(string groupName)
    {
        var users = _tracker.GetGroupUsers(groupName).ToList();
        _logger.LogDebug("Broadcasting online users update to group {GroupName} after session expiration. User count: {UserCount}", groupName, users.Count);
        await _hubContext.Clients.Group(groupName).SendAsync("UpdateOnlineUsers", users);
    }
}
