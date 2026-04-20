using Linteum.BlazorApp.Client;
using Linteum.BlazorApp.Client.Components.Notification;
using Linteum.Shared;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http.Json;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Fetch public-facing config from the Blazor server host so the WASM client
// knows where to reach the API (public URL, not the internal Docker address).
using var configHttp = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
ClientBootstrapConfig? cfg = null;
try { cfg = await configHttp.GetFromJsonAsync<ClientBootstrapConfig>("/client-config"); }
catch { /* fallback to host origin */ }

var publicApiUrl = builder.HostEnvironment.BaseAddress;
if (!string.IsNullOrWhiteSpace(cfg?.PublicApiUrl)
    && Uri.TryCreate(cfg.PublicApiUrl, UriKind.Absolute, out var publicApiUri))
{
    publicApiUrl = publicApiUri.ToString();
}

// ApiBaseUrl in configuration lets CanvasPage read the same key in both server
// and WASM contexts (server reads appsettings.json; WASM reads this override).
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ApiBaseUrl"] = publicApiUrl,
});

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(publicApiUrl) });
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<SidebarStateService>();
builder.Services.AddScoped<MyApiClient>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton(new Config
{
    GoogleClientId = cfg?.GoogleClientId ?? string.Empty,
});

await builder.Build().RunAsync();

internal record ClientBootstrapConfig(string PublicApiUrl, string GoogleClientId);


