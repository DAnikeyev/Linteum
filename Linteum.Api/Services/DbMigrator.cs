using Linteum.Infrastructure;
using Linteum.Shared;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Linteum.Api.Services
{
    public class DbMigrator
    {
        private const int DefaultMigrationMaxAttempts = 6;
        private const int DefaultMigrationRetryDelaySeconds = 10;

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DbMigrator> _logger;
        private readonly ILogger<DbSeeder> _loggerForSeeding;

        public DbMigrator(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = loggerFactory.CreateLogger<DbMigrator>();
            _loggerForSeeding = loggerFactory.CreateLogger<DbSeeder>();
        }

        public async Task InitializeAsync()
        {
            try
            {
                using var initializationScope = _serviceProvider.CreateScope();
                var initializationServices = initializationScope.ServiceProvider;

                var configuration = initializationServices.GetRequiredService<IConfiguration>();
                await EnsureCollationVersionAsync(
                    ServiceCollectionExtensions.GetRequiredConnectionString(configuration),
                    ServiceCollectionExtensions.GetMaintenanceConnectionString(configuration));

                await MigrateWithRetryAsync();

                using var seedingScope = _serviceProvider.CreateScope();
                var seedingServices = seedingScope.ServiceProvider;

                var context = seedingServices.GetRequiredService<AppDbContext>();
                var mapper = seedingServices.GetRequiredService<IMapper>();
                var dbConfig = seedingServices.GetRequiredService<Config>();

                _logger.LogInformation("Starting database seeding...");
                var repoManager = seedingServices.GetRequiredService<RepositoryManager>();
                await DbSeeder.SeedDefaults(context, dbConfig, mapper, repoManager, _loggerForSeeding);

                _logger.LogInformation("Database initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while migrating or seeding the database");
                throw;
            }
        }

        private async Task MigrateWithRetryAsync()
        {
            var maxAttempts = GetPositiveIntFromEnvironment("DB_MIGRATION_MAX_ATTEMPTS", DefaultMigrationMaxAttempts);
            var retryDelay = TimeSpan.FromSeconds(GetPositiveIntFromEnvironment("DB_MIGRATION_RETRY_DELAY_SECONDS", DefaultMigrationRetryDelaySeconds));

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var migrationScope = _serviceProvider.CreateScope();
                    var services = migrationScope.ServiceProvider;
                    var context = services.GetRequiredService<AppDbContext>();

                    _logger.LogInformation("Starting database migration (attempt {Attempt}/{MaxAttempts})...", attempt, maxAttempts);
                    await context.Database.MigrateAsync();
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts && IsTransientMigrationFailure(ex))
                {
                    _logger.LogWarning(
                        ex,
                        "Database migration attempt {Attempt}/{MaxAttempts} failed due to a transient error. Retrying in {DelaySeconds} seconds.",
                        attempt,
                        maxAttempts,
                        retryDelay.TotalSeconds);

                    await Task.Delay(retryDelay);
                }
            }
        }

        private async Task EnsureCollationVersionAsync(string appConnectionString, string maintenanceConnectionString)
        {
            var appDatabaseName = GetRequiredDatabaseName(appConnectionString, "DefaultConnection");
            var maintenanceDatabaseName = GetRequiredDatabaseName(maintenanceConnectionString, "MaintenanceConnection");

            if (!string.Equals(appDatabaseName, maintenanceDatabaseName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:MaintenanceConnection must target the same database as ConnectionStrings:DefaultConnection. DefaultConnection={appDatabaseName}, MaintenanceConnection={maintenanceDatabaseName}.");
            }

            var state = await GetCollationVersionStateAsync(maintenanceConnectionString);

            if (state == null)
            {
                _logger.LogDebug("Skipping collation version maintenance because the current database could not be resolved.");
                return;
            }

            if (string.IsNullOrWhiteSpace(state.StoredVersion) || string.IsNullOrWhiteSpace(state.ActualVersion))
            {
                _logger.LogInformation(
                    "Skipping collation version maintenance for database {DatabaseName}. Stored version: {StoredVersion}, actual version: {ActualVersion}.",
                    state.DatabaseName,
                    state.StoredVersion ?? "<null>",
                    state.ActualVersion ?? "<null>");
                return;
            }

            if (string.Equals(state.StoredVersion, state.ActualVersion, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "PostgreSQL collation version is up to date for database {DatabaseName}: {Version}",
                    state.DatabaseName,
                    state.StoredVersion);
                return;
            }

            _logger.LogWarning(
                "PostgreSQL collation version mismatch detected for database {DatabaseName}. Stored version: {StoredVersion}. Actual version: {ActualVersion}. Refreshing collation version and reindexing without deleting data.",
                state.DatabaseName,
                state.StoredVersion,
                state.ActualVersion);

            NpgsqlConnection.ClearAllPools();

            var maintenanceBuilder = new NpgsqlConnectionStringBuilder(maintenanceConnectionString)
            {
                Pooling = false,
                Enlist = false
            };

            await using var maintenanceConnection = new NpgsqlConnection(maintenanceBuilder.ConnectionString);
            await maintenanceConnection.OpenAsync();

            await using (var refreshCommand = new NpgsqlCommand(
                             $"ALTER DATABASE {QuoteIdentifier(state.DatabaseName)} REFRESH COLLATION VERSION;",
                             maintenanceConnection))
            {
                await refreshCommand.ExecuteNonQueryAsync();
            }

            await using (var reindexCommand = new NpgsqlCommand(
                             $"REINDEX DATABASE {QuoteIdentifier(state.DatabaseName)};",
                             maintenanceConnection))
            {
                await reindexCommand.ExecuteNonQueryAsync();
            }

            var refreshedState = await GetCollationVersionStateAsync(maintenanceBuilder.ConnectionString);
            if (refreshedState == null || !string.Equals(refreshedState.StoredVersion, refreshedState.ActualVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Collation version mismatch still exists for database '{state.DatabaseName}' after refresh/reindex.");
            }

            _logger.LogInformation(
                "PostgreSQL collation metadata refreshed and indexes rebuilt successfully for database {DatabaseName}. Current version: {Version}",
                refreshedState.DatabaseName,
                refreshedState.StoredVersion);
        }

        private static async Task<CollationVersionState?> GetCollationVersionStateAsync(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Pooling = false,
                Enlist = false
            };

            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            const string sql = """
                               SELECT current_database(),
                                      d.datcollversion::text,
                                      pg_database_collation_actual_version(d.oid)::text
                               FROM pg_database d
                               WHERE d.datname = current_database();
                               """;

            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new CollationVersionState(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        private static string GetRequiredDatabaseName(string connectionString, string connectionName)
        {
            var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                throw new InvalidOperationException($"{connectionName} does not specify a database name.");
            }

            return databaseName;
        }

        private static int GetPositiveIntFromEnvironment(string variableName, int fallbackValue)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            return int.TryParse(value, out var parsedValue) && parsedValue > 0
                ? parsedValue
                : fallbackValue;
        }

        private static bool IsTransientMigrationFailure(Exception exception)
        {
            if (exception is TimeoutException)
            {
                return true;
            }

            if (exception is InvalidOperationException invalidOperationException &&
                invalidOperationException.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (exception is NpgsqlException npgsqlException &&
                (npgsqlException.InnerException is TimeoutException ||
                 npgsqlException.Message.Contains("reading from stream", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (exception.Message.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("LOCK TABLE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return exception.InnerException is not null && IsTransientMigrationFailure(exception.InnerException);
        }

        private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

        private sealed record CollationVersionState(string DatabaseName, string? StoredVersion, string? ActualVersion);
    }
}