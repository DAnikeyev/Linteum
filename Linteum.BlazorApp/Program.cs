using Linteum.BlazorApp.Client;
using Linteum.BlazorApp.Client.Components.Notification;
using Linteum.BlazorApp.Components;
using Linteum.Shared;
using Microsoft.AspNetCore.DataProtection;
using NLog;
using NLog.Web;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    DotNetEnv.Env.Load("../.env");
    var builder = WebApplication.CreateBuilder(args);

    var version = builder.Configuration["VERSION"] ?? Environment.GetEnvironmentVariable("VERSION") ?? "dev";
    logger.Info("Application version: {Version}", version);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

#if DEBUG
    var apiContainerName = "localhost";
    var apiContainerPort = "5182";
#else
    var apiContainerName = Environment.GetEnvironmentVariable("API_CONTAINER_NAME") ?? "api";
    var apiContainerPort = Environment.GetEnvironmentVariable("API_CONTAINER_PORT") ?? "8080";
#endif
    var apiBaseAddress = $"http://{apiContainerName}:{apiContainerPort}";
    logger.Info("API Base Address configured: {ApiBaseAddress}", apiBaseAddress);

    builder.Services.AddHttpClient("ApiClient", client =>
    {
        client.BaseAddress = new Uri(apiBaseAddress);
    }).ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (builder.Environment.IsDevelopment())
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        return handler;
    });
    logger.Info("HttpClient 'ApiClient' configured");

    builder.Services.AddSingleton(new Config
    {
        GoogleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty,
    });
    builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));
    builder.Services.AddScoped<MyApiClient>();
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")))
        .SetApplicationName("LinteumApp");
    builder.Services.AddScoped<LocalStorageService>();
    builder.Services.AddScoped<SidebarStateService>();
    builder.Services.AddScoped<NotificationService>();

    logger.Info("Core services configured");

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents()
        .AddInteractiveWebAssemblyComponents();

    logger.Info("Razor Components (Server + WASM) added");

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // Endpoint for the WASM client to discover the public API URL and Google client ID
    var publicApiUrl = Environment.GetEnvironmentVariable("PUBLIC_API_URL");
    if (string.IsNullOrWhiteSpace(publicApiUrl))
        publicApiUrl = apiBaseAddress;
    var googleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty;
    app.MapGet("/client-config", () => new { PublicApiUrl = publicApiUrl, GoogleClientId = googleClientId });

    app.UseAntiforgery();
    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(ClientAssemblyMarker).Assembly);

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
