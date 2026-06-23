using Microsoft.AspNetCore.Http;

namespace Linteum.Tests.Db.Api;

/// <summary>
/// Integration tests for the session-auth gate (P‑SEC‑01, P‑TEST‑02). Every controller action
/// requires a valid <c>Session-Id</c> header unless it opts out with <c>[PublicEndpoint]</c>;
/// <c>[DisabledEndpoint]</c> actions return 404. These run against the real booted API.
/// </summary>
[TestFixture]
public class SessionAuthMiddlewareTests : ApiTestBase
{
    [Test]
    public async Task Protected_endpoint_without_session_returns_401()
    {
        // GET /Canvases/name/{name} is neither public nor disabled, so it must require a session.
        var response = await Client.GetAsync("/Canvases/name/anything");

        Assert.That((int)response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
    }

    [Test]
    public async Task Public_endpoint_is_reachable_without_session()
    {
        // GET /Colors is [PublicEndpoint]; no session header required.
        var response = await Client.GetAsync("/Colors");

        Assert.That((int)response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    [Test]
    public async Task Disabled_endpoint_returns_404()
    {
        // SubscriptionsController is [DisabledEndpoint] at the class level.
        var response = await Client.GetAsync($"/Subscriptions/user/{Guid.NewGuid()}");

        Assert.That((int)response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }
}
