using Linteum.Infrastructure;
using NLog;
using NLog.Web;
using Linteum.Api.Extensions;
using Linteum.Api.Services;

var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add NLog
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Add application services (moved to extension method)
    builder.Services.AddApplicationServices(builder.Configuration);

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // Apply migrations and seed data at startup using DBMigrator service
    using (var scope = app.Services.CreateScope())
    {
        var dbMigrator = scope.ServiceProvider.GetRequiredService<DBMigrator>();
        await dbMigrator.InitializeAsync();
    }

    app.UseHttpsRedirection();

    // GET /colors endpoint
    app.MapGet("/colors", async (IServiceProvider serviceProvider) =>
        {
            using var scope = serviceProvider.CreateScope();
            var repoManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();
            var colors = await repoManager.ColorRepository.GetAllAsync();
            return Results.Ok(colors);
        })
        .WithName("GetColors");

    app.Run();
}
catch (Exception exception)
{
    logger.Error(exception, "Stopped program because of exception");
    throw;
}
finally
{
    NLog.LogManager.Shutdown();
}