namespace Linteum.Api.Attributes;

/// <summary>
/// Marks a controller action (or controller) as not requiring a validated session.
/// Applied to login/signup/validate/guest/google-login and the public palette endpoint.
/// Without this attribute, every controller action is session-gated by
/// <c>SessionAuthMiddleware</c> (P-SEC-01).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PublicEndpointAttribute : Attribute
{
}

/// <summary>
/// Marks a controller action (or controller) as disabled: the middleware returns 404.
/// Used to make endpoints that are not used by the frontend unreachable, while keeping
/// them easy to re-enable (just remove the attribute). Honors class-level application
/// for whole controllers (P-SEC-01).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class DisabledEndpointAttribute : Attribute
{
}
