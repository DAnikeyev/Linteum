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
    var apiContainerPort = "5182";
#else
    var apiContainerName = Environment.GetEnvironmentVariable("API_CONTAINER_NAME") ?? "api";
    var apiContainerPort = Environment.GetEnvironmentVariable("API_CONTAINER_PORT") ?? "8080";
#endif
    var apiBaseAddress = $"http://{apiContainerName}:{apiContainerPort}";

    logger.Info("API Base Address configured: {ApiBaseAddress}", apiBaseAddress);

    builder.Services.AddHttpClient<MyApiClient>("ApiClient", client => {
        client.BaseAddress = new Uri(apiBaseAddress);
    }).ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (builder.Environment.IsDevelopment())
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    });
    logger.Info("HttpClient 'ApiClient' configured");
    
    builder.Services.AddSingleton(new Config());
    builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "keys")))
        .SetApplicationName("LinteumApp");
    builder.Services.AddScoped<LocalStorageService>();
    builder.Services.AddScoped<NotificationService>();
    
    logger.Info("Core services (DataProtection, LocalStorage, Notification) configured");

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    
    logger.Info("Razor Components and Interactive Server Components added");

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