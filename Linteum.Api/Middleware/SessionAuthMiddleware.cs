using Linteum.Api.Attributes;
using Linteum.Api.Services;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Linteum.Api.Middleware;

/// <summary>
/// Centralizes session authentication (P-SEC-01), replacing per-method
/// <c>SessionService.ProcessHeader</c>/<c>GetUserIdAndUpdateTimeLimit</c> checks.
///
/// By default every controller action requires a valid <c>Session-Id</c> header. Endpoints
/// opt out with <c>[PublicEndpoint]</c>; disabled endpoints return 404 via
/// <c>[DisabledEndpoint]</c>. Non-controller endpoints (SignalR hub, OpenAPI) and CORS
/// preflight are passed through untouched. On success the resolved user id is stored in
/// <see cref="HttpContext.Items"/> for controllers to read via
/// <see cref="SessionAuthHttpContextExtensions.GetSessionUserId"/>.
/// </summary>
public sealed class SessionAuthMiddleware
{
    public const string SessionUserIdItemKey = "__SessionUserId";

    private readonly RequestDelegate _next;
    private readonly SessionService _sessionService;
    private readonly ILogger<SessionAuthMiddleware> _logger;

    public SessionAuthMiddleware(RequestDelegate next, SessionService sessionService, ILogger<SessionAuthMiddleware> logger)
    {
        _next = next;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // CORS preflight is short-circuited earlier by UseCors; be defensive anyway.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();

        // Only gate routed controller actions (leaves the SignalR hub, OpenAPI, etc. alone).
        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (endpoint is null || action is null)
        {
            await _next(context);
            return;
        }

        if (endpoint.Metadata.GetMetadata<DisabledEndpointAttribute>() is not null)
        {
            _logger.LogDebug("Denied request to disabled endpoint {Controller}.{Action}.", action.ControllerName, action.ActionName);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (endpoint.Metadata.GetMetadata<PublicEndpointAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        var userId = _sessionService.ProcessHeader(context.Request.Headers);
        if (!userId.HasValue)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Session-Id header missing or invalid.");
            return;
        }

        context.Items[SessionUserIdItemKey] = userId.Value;
        await _next(context);
    }
}

public static class SessionAuthHttpContextExtensions
{
    /// <summary>Returns the user id resolved by <see cref="SessionAuthMiddleware"/>, or null.</summary>
    public static Guid? GetSessionUserId(this HttpContext context)
    {
        if (context.Items.TryGetValue(SessionAuthMiddleware.SessionUserIdItemKey, out var value) && value is Guid userId)
        {
            return userId;
        }

        return null;
    }
}
