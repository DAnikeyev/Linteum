using Linteum.Api.Configuration;
using Linteum.Api.Hubs;
using Linteum.Api.Services;
using Linteum.Infrastructure;
using NLog;
using NLog.Web;

ThreadPool.SetMinThreads(25, 25);

DotNetEnv.Env.Load("../.env");

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
var consoleMinLevel = Environment.GetEnvironmentVariable("NLOG_CONSOLE_MIN_LEVEL") ?? "Info";
logger.Info("NLog console minimum level on startup: {MinLevel}", consoleMinLevel);
logger.Debug("Debug startup logging is enabled.");

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    var allowedOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() 
                         ?? Array.Empty<string>();
    
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowBlazorApp", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });
    logger.Info("CORS policy 'AllowBlazorApp' added with origins: {Origins}", string.Join(", ", allowedOrigins));
    
    builder.Services.AddSignalR();
    logger.Info("SignalR service added");
    builder.Services.AddHttpClient();
    
    builder.Services.AddSingleton<IConnectionTracker, ConnectionTracker>();
    builder.Services.AddSingleton<ICanvasChatBroadcaster, CanvasChatBroadcaster>();
    builder.Services.AddScoped<IPixelNotifier, SignalRPixelNotifier>();
    builder.Services.Configure<CanvasSizeOptions>(builder.Configuration.GetSection("CanvasSize"));
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddControllers();
    logger.Info("Application services and controllers added");

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
    if (app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }
    app.UseCors("AllowBlazorApp");
    app.MapControllers();
    app.MapHub<CanvasHub>("/canvashub");
    
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
