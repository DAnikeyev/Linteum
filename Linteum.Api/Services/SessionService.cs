using System.Collections.Concurrent;
using Linteum.Domain;
using Linteum.Shared;

namespace Linteum.Api.Services;

public class SessionService
{
    private readonly ILogger<SessionService> _logger;
    private readonly ConcurrentDictionary<Guid, UserSession> _sessionToUser = new();
    private readonly ConcurrentDictionary<Guid, Guid> _userToSession = new();
    private readonly TimeSpan _expiredSessionTimeout;

    public SessionService(Config config, ILogger<SessionService> logger)
    {
        _logger = logger;
        _expiredSessionTimeout = TimeSpan.FromMinutes(config.ExpiredSessionTimeoutMinutes);
    }

    public Guid CreateSession(Guid userId)
    {
        var sessionId = Guid.NewGuid();

        if (_userToSession.TryRemove(userId, out var oldSessionId))
        {
            _sessionToUser.TryRemove(oldSessionId, out _);
        }

        var session = new UserSession
        {
            SessionId = sessionId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        _sessionToUser[sessionId] = session;
        _userToSession[userId] = sessionId;
        _logger.LogInformation($"Created new session for user {userId} with session ID {sessionId} at {session.CreatedAt}");
        return sessionId;
    }

    public Guid? GetUserId(Guid sessionId)
    {
        if (_sessionToUser.TryGetValue(sessionId, out var session))
        {
            if(session.CreatedAt + _expiredSessionTimeout > DateTime.UtcNow)
                return session.UserId;
            RemoveSession(sessionId);
        }
        return null;
    }

    public void RemoveSession(Guid sessionId)
    {
        if (_sessionToUser.TryRemove(sessionId, out var session))
        {
            _userToSession.TryRemove(session.UserId, out _);
        }
    }

    public void CleanupExpiredSessions()
    {
        var expired = _sessionToUser.Where(s => s.Value.CreatedAt + _expiredSessionTimeout <= DateTime.UtcNow).ToList();
        foreach (var session in expired)
        {
            _sessionToUser.TryRemove(session.Key, out _);
            _userToSession.TryRemove(session.Value.UserId, out _);
        }
    }
}