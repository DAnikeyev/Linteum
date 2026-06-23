using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Linteum.Tests.Db.Api;

/// <summary>
/// Boots the real Linteum API in-process (TestServer) for integration tests (P‑TEST‑02).
///
/// The API host runs the full startup pipeline — including <c>DbMigrator</c>, which migrates
/// and seeds the database — so the env vars it reads at startup must be in place before the host
/// builds (the host builds lazily on first <c>CreateClient</c> call). The database points at the
/// migration-based <c>linteum_api_tests</c> database in the shared Testcontainers Postgres
/// (<see cref="DbTestSetup"/>), separate from the EF-model database the repository tests use.
/// </summary>
public class LinteumWebAppFactory : WebApplicationFactory<Program>
{
    public LinteumWebAppFactory()
    {
        Environment.SetEnvironmentVariable("MASTER_PASSWORD", "test-master-password");
        Environment.SetEnvironmentVariable("MASTER_USER", "admin");
        Environment.SetEnvironmentVariable("MASTER_EMAIL", "admin@linteum-test.local");
        Environment.SetEnvironmentVariable("DEFAULT_DB_HOST_CONNECTION", DbTestSetup.ApiConnectionString);
        Environment.SetEnvironmentVariable("NLOG_CONSOLE_MIN_LEVEL", "Warn");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (not "Development") skips OpenAPI mapping and HTTPS redirection; the factory
        // talks plain HTTP to the in-memory TestServer.
        builder.UseEnvironment("Testing");
        base.ConfigureWebHost(builder);
    }
}

/// <summary>Builds the API host once per test fixture and exposes an HttpClient.</summary>
public abstract class ApiTestBase
{
    protected LinteumWebAppFactory Factory = null!;
    protected HttpClient Client = null!;

    [OneTimeSetUp]
    public void CreateApiClient()
    {
        Factory = new LinteumWebAppFactory();
        Client = Factory.CreateClient();
    }

    [OneTimeTearDown]
    public void DisposeApiClient()
    {
        Client?.Dispose();
        Factory?.Dispose();
    }
}
