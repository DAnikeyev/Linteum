using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using Linteum.Infrastructure;

namespace Linteum.Tests
{
    [TestFixture]
    public class DatabaseTests
    {
        [Test]
        public void CanCreateAndDeleteDatabase()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=efcore_test_db;Username=postgres;Password=password") // Use PostgreSQL
                .Options;

            using (var context = new AppDbContext(options))
            {
                context.Database.EnsureDeleted();
                Assert.IsFalse(context.Database.CanConnect());
                context.Database.EnsureCreated();
                Assert.IsTrue(context.Database.CanConnect());
            }
        }
    }
}
