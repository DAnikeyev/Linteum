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
    
    public bool ValidateSession(Guid sessionId)
    {
        if (_sessionToUser.TryGetValue(sessionId, out var session))
        {
            if (session.CreatedOrUpdatedAt + _expiredSessionTimeout > DateTime.UtcNow)
            {
                _logger.LogDebug("Session {SessionId} is valid for user {UserId}", sessionId, session.UserId);
                return true;
            }

            RemoveSession(sessionId);
            _logger.LogDebug("Session {SessionId} has expired for user {UserId}", sessionId, session.UserId);
        }
        else
        {
            _logger.LogDebug("Session {SessionId} not found", sessionId);
        }
        return false;
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
            CreatedOrUpdatedAt = DateTime.UtcNow,
        };

        _sessionToUser[sessionId] = session;
        _userToSession[userId] = sessionId;
        _logger.LogDebug("Created new session for user {UserId} with session ID {SessionId} at {CreatedOrUpdatedAt}", userId, sessionId, session.CreatedOrUpdatedAt);
        return sessionId;
    }

    public Guid? GetUserIdAndUpdateTimeLimit(Guid sessionId)
    {
        if (_sessionToUser.TryGetValue(sessionId, out var session))
        {
            if (session.CreatedOrUpdatedAt + _expiredSessionTimeout > DateTime.UtcNow)
            {
                session.CreatedOrUpdatedAt = DateTime.UtcNow;
                _logger.LogDebug("Session {SessionId} is valid for user {UserId}", sessionId, session.UserId);
                return session.UserId;
            }

            RemoveSession(sessionId);
            _logger.LogDebug("Session {SessionId} has expired for user {UserId}", sessionId, session.UserId);
        }
        else
        {
            _logger.LogDebug("Session {SessionId} not found", sessionId);
        }
        return null;
    }

    public Guid? ProcessHeader(IHeaderDictionary header)
    {
        var sessionIdString = header[CustomHeaders.SessionId];
        if (string.IsNullOrEmpty(sessionIdString) || !Guid.TryParse(sessionIdString, out var sessionId))
        {
            _logger.LogDebug("Session-Id header missing or invalid.");
            return null;
        }
        return GetUserIdAndUpdateTimeLimit(sessionId);
    }

    public void RemoveSession(Guid sessionId)
    {
        if (_sessionToUser.TryRemove(sessionId, out var session))
        {
            _userToSession.TryRemove(session.UserId, out _);
        }
    }

    public List<UserSession> CleanupExpiredSessions()
    {
        var expired = _sessionToUser.Where(s => s.Value.CreatedOrUpdatedAt + _expiredSessionTimeout <= DateTime.UtcNow).ToList();
        foreach (var session in expired)
        {
            _sessionToUser.TryRemove(session.Key, out _);
            _userToSession.TryRemove(session.Value.UserId, out _);
        }
        return expired.Select(s => s.Value).ToList();
    }
}