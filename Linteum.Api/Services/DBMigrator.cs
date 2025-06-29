using Linteum.Infrastructure;
using Linteum.Domain.Repository;
using Linteum.Shared;
using AutoMapper;
using Microsoft.EntityFrameworkCore;

namespace Linteum.Api.Services
{
    public class DBMigrator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DBMigrator> _logger;
        private readonly ILogger<DbSeeder> _loggerForSeeding;

        public DBMigrator(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = loggerFactory.CreateLogger<DBMigrator>();
            _loggerForSeeding = loggerFactory.CreateLogger<DbSeeder>();
        }

        public async Task InitializeAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var context = services.GetRequiredService<AppDbContext>();
                var mapper = services.GetRequiredService<IMapper>();
                var canvasRepository = services.GetRequiredService<ICanvasRepository>();
                var dbConfig = services.GetRequiredService<DbConfig>();

                _logger.LogInformation("Starting database migration...");
                await context.Database.MigrateAsync();

                _logger.LogInformation("Starting database seeding...");
                DbSeeder.SeedDefaults(context, dbConfig, mapper, canvasRepository, _loggerForSeeding);

                _logger.LogInformation("Database initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while migrating or seeding the database");
                throw;
            }
        }
    }
}