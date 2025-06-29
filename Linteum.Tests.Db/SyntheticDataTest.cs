using Linteum.Infrastructure;
using Linteum.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Tests.Db;

internal class SyntheticDataTest
{
    protected AppDbContext DbContext;
    protected DbContextOptions<AppDbContext> Options;
    protected DbHelper DbHelper;
    protected DbConfig DefaultConfig = new DbConfig();
    
    internal RepositoryManager RepoManager => DbHelper.RepositoryManager;
    
    //ToDo: Clear instead of delete.
    [SetUp]
    public virtual void Init()
    {
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=efcore_test_db;Username=postgres;Password=password")
            .Options;
        DbContext = new AppDbContext(Options);
        DbHelper = new DbHelper(DbContext);
        DbContext.Database.EnsureDeleted();
        DbContext.Database.EnsureCreated();
        DbSeeder.SeedDefaults(DbContext,new DbConfig(), DbHelper.Mapper, RepoManager.CanvasRepository, DbHelper.LoggerFactoryInterface.CreateLogger<DbSeeder>());
        SeedData();
    }

    protected virtual void SeedData()
    {
    }

    [TearDown]
    public virtual void Cleanup()
    {
        DbContext.Database.EnsureDeleted();
        DbContext.Dispose();
    }
    
    
}