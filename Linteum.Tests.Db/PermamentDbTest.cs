using Linteum.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Linteum.Tests.Db;

public class PermamentDbTest
{
    [Test]
    [Explicit]
    public void CreatePermDataBase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=efcore_test_db_permament;Username=postgres;Password=password") // Use PostgreSQL
            .Options;

        using (var context = new AppDbContext(options))
        {
            context.Database.EnsureCreated();
            Assert.IsTrue(context.Database.CanConnect());
        }
    }
    
    [Explicit]
    [Test]
    public void DeletePermDataBase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=efcore_test_db_permament;Username=postgres;Password=password") // Use PostgreSQL
            .Options;

        using (var context = new AppDbContext(options))
        {
            context.Database.EnsureDeleted();
            Assert.IsFalse(context.Database.CanConnect());
        }
    }
}