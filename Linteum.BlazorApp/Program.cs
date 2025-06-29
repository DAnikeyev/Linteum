using Linteum.BlazorApp;
using Linteum.BlazorApp.Components;

var builder = WebApplication.CreateBuilder(args);
DotNetEnv.Env.Load("../.env");
// Read the API base address from the environment variable
var apiContainerName = Environment.GetEnvironmentVariable("API_CONTAINER_NAME") ?? "api";
var apiContainerPort = Environment.GetEnvironmentVariable("API_CONTAINER_PORT") ?? "8080";
var apiBaseAddress = $"http://{apiContainerName}:{apiContainerPort}";

Console.WriteLine($"API Base Address: {apiBaseAddress}");

builder.Services.AddHttpClient<MyApiClient>("ApiClient", client => {
    client.BaseAddress = new Uri(apiBaseAddress);
});

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();