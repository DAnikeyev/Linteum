using Linteum.BlazorApp;
using Linteum.BlazorApp.Components;
using Linteum.BlazorApp.Components.Notification;
using Linteum.Shared;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using NLog;
using NLog.Web;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure NLog
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    DotNetEnv.Env.Load("../.env");

    builder.Services.AddScoped<ProtectedLocalStorage>();

#if DEBUG
    var apiContainerName = "localhost";
    var apiContainerPort = "8080";
#else
    var apiContainerName = Environment.GetEnvironmentVariable("API_CONTAINER_NAME") ?? "api";
    var apiContainerPort = Environment.GetEnvironmentVariable("API_CONTAINER_PORT") ?? "8080";
#endif
    var apiBaseAddress = $"http://{apiContainerName}:{apiContainerPort}";

    logger.Info($"API Base Address: {apiBaseAddress}");

    builder.Services.AddHttpClient<MyApiClient>("ApiClient", client => {
        client.BaseAddress = new Uri(apiBaseAddress);
    });
    
    builder.Services.AddSingleton(new Config());
    builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "keys")))
        .SetApplicationName("LinteumApp");
    builder.Services.AddScoped<LocalStorageService>();
    builder.Services.AddScoped<NotificationService>();
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseAntiforgery();
    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

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
    LogManager.Shutdown();
}