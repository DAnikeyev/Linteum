using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public abstract class BotBase
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(300);

    protected readonly HttpClient HttpClient;
    protected readonly string ApiUrl;
    protected string? MasterPassword { get; }
    protected string BotEmail { get; }
    protected string BotPassword { get; }
    protected string BotUserName { get; }
    private long _requestCount;

    protected BotBase(string email, string password, string userName)
    {
        BotEmail = email;
        BotPassword = password;
        BotUserName = userName;
        ApiUrl = Environment.GetEnvironmentVariable("BOT_API_URL") ?? "http://localhost:5182";
        MasterPassword = Environment.GetEnvironmentVariable("BOT_MASTER_PASSWORD");

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        HttpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"Starting {BotUserName}...");

        Guid? sessionId = await LoginOrRegisterAsync();
        if (sessionId == null)
        {
            Console.WriteLine("Failed to login or register.");
            return;
        }
        
        HttpClient.DefaultRequestHeaders.Add(CustomHeaders.SessionId, sessionId.ToString());
        Console.WriteLine("Logged in successfully.");

        var colors = await GetColorsAsync();
        if (colors == null || colors.Count == 0)
        {
            Console.WriteLine("No colors available.");
            return;
        }

        var canvas = await GetOrCreateCanvasAsync();
        if (canvas == null)
        {
             Console.WriteLine("Could not access canvas.");
             return;
        }

        Console.WriteLine($"Canvas '{canvas.Name}' ready. Id: {canvas.Id}");

        var timeout = DefaultTimeout;
        var envMinutes = Environment.GetEnvironmentVariable("BOT_TIMEOUT_MINUTES");
        if (double.TryParse(envMinutes, out var minutes))
            timeout = TimeSpan.FromMinutes(minutes);

        using var cts = new CancellationTokenSource(timeout);
        var runToken = cts.Token;
        Console.WriteLine($"Bot will run for up to {timeout.TotalMinutes} minute(s).");

        _ = Task.Run(async () =>
        {
            try
            {
                while (!runToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, runToken);
                    long currentCount = Interlocked.Exchange(ref _requestCount, 0);
                    Console.WriteLine($"[{BotUserName}] Requests per second: {currentCount}");
                }
            }
            catch (OperationCanceledException) { }
        });

        try
        {
            await RunBehaviorAsync(canvas, colors, cts.Token);
            Console.WriteLine($"[{BotUserName}] Finished.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{BotUserName}] Timed out after {timeout.TotalMinutes} minute(s). Exiting.");
        }
    }

    protected abstract Task<CanvasDto?> GetOrCreateCanvasAsync();
    
    protected abstract Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors, CancellationToken ct);

    protected async Task<List<ColorDto>?> GetColorsAsync()
    {
        return await HttpClient.GetFromJsonAsync<List<ColorDto>>("Colors");
    }

    protected async Task<Guid?> LoginOrRegisterAsync()
    {
        var loginDto = new UserDto
        {
            Email = BotEmail,
            LoginMethod = LoginMethod.Password
        };

        var loginRes = await HttpClient.PostAsJsonAsync($"Users/login?passwordHashOrKey={BotPassword}", loginDto);
        if (loginRes.IsSuccessStatusCode)
        {
            var result = await loginRes.Content.ReadFromJsonAsync<LoginResponse>();
            return result?.SessionId;
        }

        if (loginRes.StatusCode == System.Net.HttpStatusCode.Unauthorized || loginRes.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine("Login failed, trying to register...");
            var registerDto = new UserDto
            {
                Email = BotEmail,
                UserName = BotUserName,
                LoginMethod = LoginMethod.Password
            };
            
            var regRes = await HttpClient.PostAsJsonAsync($"Users/add?passwordHashOrKey={BotPassword}", registerDto);
            if (regRes.IsSuccessStatusCode)
            {
                 var result = await regRes.Content.ReadFromJsonAsync<LoginResponse>();
                 return result?.SessionId;
            }
            else
            {
                 Console.WriteLine($"Registration failed: {regRes.StatusCode}");
                 var content = await regRes.Content.ReadAsStringAsync();
                 Console.WriteLine(content);
            }
        }
        else
        {
             Console.WriteLine($"Login error: {loginRes.StatusCode}");
        }

        return null;
    }
    
    protected async Task<bool> TryPaintPixelAsync(CanvasDto canvas, int x, int y, int colorId, CancellationToken ct = default)
    {
        var result = await TryPaintPixelsAsync(canvas,
        [
            new PixelDto
            {
                X = x,
                Y = y,
                ColorId = colorId,
                CanvasId = canvas.Id,
            }
        ], ct);

        return result is { ChangedPixels.Count: > 0 };
    }

    protected async Task<PixelBatchChangeResultDto?> TryPaintPixelsAsync(CanvasDto canvas, IReadOnlyCollection<PixelDto> pixels, CancellationToken ct = default)
    {
        Interlocked.Add(ref _requestCount, pixels.Count);
        var requestDto = new PixelBatchChangeRequestDto
        {
            MasterPassword = MasterPassword,
            Pixels = pixels.Select(pixel => new PixelDto
            {
                Id = pixel.Id,
                X = pixel.X,
                Y = pixel.Y,
                ColorId = pixel.ColorId,
                OwnerId = pixel.OwnerId,
                Price = pixel.Price,
                CanvasId = canvas.Id,
            }).ToList(),
        };

        try
        {
            using var response = await HttpClient.PostAsJsonAsync($"Pixels/change-batch/{canvas.Name}", requestDto, ct);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to paint batch on {canvas.Name}: {response.StatusCode}");
                var content = await response.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    Console.WriteLine(content);
                }
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PixelBatchChangeResultDto>(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"Painting batch on {canvas.Name} timed out.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error painting batch: {ex.Message}");
            return null;
        }
    }

    protected async Task PaintPixelAsync(CanvasDto canvas, int x, int y, int colorId)
    {
        await TryPaintPixelAsync(canvas, x, y, colorId);
    }
}

