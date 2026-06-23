using Linteum.Infrastructure;
using Linteum.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Linteum.Tests.Db;

internal abstract class SyntheticDataTest
{
    protected AppDbContext DbContext;
    protected DbContextOptions<AppDbContext> Options;
    protected DbHelper DbHelper;
    protected Config DefaultConfig = new Config();
    
    internal RepositoryManager RepoManager => DbHelper.RepositoryManager;
    
    // P‑TEST‑03/04: tests run against an ephemeral Testcontainers Postgres (see DbTestSetup).
    // Per-test isolation is a TRUNCATE … RESTART IDENTITY CASCADE of every table instead of
    // dropping and recreating the database on each test.
    [SetUp]
    public virtual async Task Init()
    {
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DbTestSetup.RepoConnectionString)
            .Options;
        DbContext = new AppDbContext(Options);
        DbHelper = new DbHelper(DbContext);

        await EnsureSchemaOnceAsync();
        await ResetDataAsync();
        await DbSeeder.SeedDefaults(DbContext, new Config(), DbHelper.Mapper, RepoManager, DbHelper.LoggerFactoryInterface.CreateLogger<DbSeeder>());
        SeedData();
    }

    protected virtual void SeedData()
    {
    }

    [TearDown]
    public virtual void Cleanup()
    {
        DbContext.Dispose();
    }

    private static int _schemaReady;

    private async Task EnsureSchemaOnceAsync()
    {
        // The schema is created from the EF model once per assembly; later tests just clear data.
        if (Interlocked.CompareExchange(ref _schemaReady, 1, 0) == 0)
        {
            await DbContext.Database.EnsureCreatedAsync();
        }
    }

    private async Task ResetDataAsync()
    {
        var tables = DbContext.Model.GetEntityTypes()
            .Select(e => (Schema: e.GetSchema() ?? "public", Table: e.GetTableName()))
            .Where(t => t.Table is not null)
            .Distinct()
            .Select(t => $"\"{t.Schema}\".\"{t.Table}\"");

        var sql = $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;";
        await DbContext.Database.ExecuteSqlRawAsync(sql);
    }
}
