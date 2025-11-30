using Linteum.Api.Configuration;
using Linteum.Infrastructure;
using NLog;
using NLog.Web;
using Linteum.Api.Services;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    builder.Services.Configure<CanvasSizeOptions>(builder.Configuration.GetSection("CanvasSize"));
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddControllers();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    using (var scope = app.Services.CreateScope())
    {
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var dbMigrator = new DbMigrator(app.Services, loggerFactory);
        await dbMigrator.InitializeAsync();
    }
    app.UseHttpsRedirection();
    app.MapControllers();
    logger.Info("Application started successfully");
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

