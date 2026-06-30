using Npgsql;
using Testcontainers.PostgreSql;

namespace Linteum.Tests.Db;

/// <summary>
/// Starts one ephemeral PostgreSQL container for the whole test assembly (P‑TEST‑03) and
/// provisions two databases inside it:
///   - <c>linteum_repo_tests</c> for the repository tests (<see cref="SyntheticDataTest"/>),
///     whose schema is created with <c>EnsureCreated</c> (the EF model);
///   - <c>linteum_api_tests</c> for the API integration tests, whose schema is created by the
///     real EF migrations run by <c>DbMigrator</c> inside the WebApplicationFactory host.
/// Using two databases keeps the model‑based and migration‑based schemas from clashing while
/// sharing a single container. NUnit runs the <c>[OneTimeSetUp]</c> once per run.
/// </summary>
[SetUpFixture]
public class DbTestSetup
{
    private const string RepoDbName = "linteum_repo_tests";
    private const string ApiDbName = "linteum_api_tests";

    private PostgreSqlContainer? _container;

    /// <summary>Connection string for the EF‑model (<c>EnsureCreated</c>) database.</summary>
    public static string RepoConnectionString { get; private set; } = null!;

    /// <summary>Connection string for the migration‑based API integration database.</summary>
    public static string ApiConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        var bootstrap = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "postgres",
        };

        await CreateDatabaseAsync(bootstrap.ConnectionString, RepoDbName);
        await CreateDatabaseAsync(bootstrap.ConnectionString, ApiDbName);

        RepoConnectionString = WithDatabase(bootstrap, RepoDbName);
        ApiConnectionString = WithDatabase(bootstrap, ApiDbName);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task CreateDatabaseAsync(string postgresConnectionString, string dbName)
    {
        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{dbName}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static string WithDatabase(NpgsqlConnectionStringBuilder template, string dbName)
        => new NpgsqlConnectionStringBuilder(template.ConnectionString) { Database = dbName }.ConnectionString;
}
