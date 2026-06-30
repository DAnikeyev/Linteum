using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public abstract class BotBase
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(300);
    protected const int MaxPaintBatchSize = 500;

    protected readonly HttpClient HttpClient;
    protected readonly string ApiUrl;
    protected string? ServiceToken { get; }
    protected string BotEmail { get; }
    protected string BotPassword { get; }
    protected string BotUserName { get; }
    private long _batchedRequestCount;
    private long _attemptedPixelCount;

    // Bot credentials are read from the environment so no secret lives in source (P-SEC-10).
    // The password is the only secret; email/userName are non-secret service-account identifiers.
    protected BotBase(string email, string userName)
    {
        BotEmail = email;
        BotUserName = userName;
        BotPassword = Environment.GetEnvironmentVariable("BOT_PASSWORD")
            ?? throw new InvalidOperationException(
                "BOT_PASSWORD is not set. Bot credentials must be provided via the environment (P-SEC-10).");

        ApiUrl = Environment.GetEnvironmentVariable("BOT_API_URL") ?? "http://localhost:8080";

        // Dedicated least-privilege secret; never the API MASTER_PASSWORD (P-SEC-11). Optional —
        // when unset the bot paints without the quota/balance override (a no-op on FreeDraw).
        ServiceToken = Environment.GetEnvironmentVariable("BOT_SERVICE_TOKEN");

        // TLS is validated by default (system trust store). The validation bypass is dev-only
        // and must be opted into explicitly via BOT_INSECURE_SKIP_TLS_VALIDATION (P-SEC-09).
        var handler = CreateHttpClientHandler();
        HttpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static HttpClientHandler CreateHttpClientHandler()
    {
        var skipTlsValidation = IsTruthyEnvironmentVariable("BOT_INSECURE_SKIP_TLS_VALIDATION");
        if (skipTlsValidation)
        {
            Console.WriteLine(
                "WARNING: BOT_INSECURE_SKIP_TLS_VALIDATION is set — TLS certificate validation is DISABLED. " +
                "Use this only for local development against a self-signed certificate.");
            return new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
        }

        return new HttpClientHandler();
    }

    private static bool IsTruthyEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
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
                    long currentRequestCount = Interlocked.Exchange(ref _batchedRequestCount, 0);
                    long currentPixelCount = Interlocked.Exchange(ref _attemptedPixelCount, 0);
                    Console.WriteLine($"[{BotUserName}] Throughput: {currentPixelCount} px/s across {currentRequestCount} batch request(s)/s");
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
        var loginRes = await HttpClient.PostAsJsonAsync("Users/login", new LoginRequestDto { Email = BotEmail, Password = BotPassword });
        if (loginRes.IsSuccessStatusCode)
        {
            var result = await loginRes.Content.ReadFromJsonAsync<LoginResponse>();
            return result?.SessionId;
        }

        if (loginRes.StatusCode == System.Net.HttpStatusCode.Unauthorized || loginRes.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine("Login failed, trying to register...");
            var regRes = await HttpClient.PostAsJsonAsync("Users/add", new SignupRequestDto { Email = BotEmail, UserName = BotUserName, Password = BotPassword });
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
        if (pixels.Count == 0)
        {
            return new PixelBatchChangeResultDto();
        }

        if (TryGetUniformBatch(pixels, out var colorId, out var price))
        {
            return await TryPaintCoordinatesAsync(
                canvas,
                pixels.Select(pixel => new CoordinateDto(pixel.X, pixel.Y)).ToList(),
                colorId,
                price,
                ct);
        }

        RecordBatchAttempt(pixels.Count);
        var requestDto = new PixelBatchChangeRequestDto
        {
            ServiceToken = ServiceToken,
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

    protected async Task<PixelBatchChangeResultDto?> TryPaintCoordinatesAsync(CanvasDto canvas, IReadOnlyCollection<CoordinateDto> coordinates, int colorId, long price = 0, CancellationToken ct = default)
    {
        if (coordinates.Count == 0)
        {
            return new PixelBatchChangeResultDto();
        }

        RecordBatchAttempt(coordinates.Count);
        var requestDto = new PixelBatchDto
        {
            ServiceToken = ServiceToken,
            Coordinates = coordinates.ToList(),
            ColorId = colorId,
            Price = price,
        };

        try
        {
            using var response = await HttpClient.PostAsJsonAsync($"Pixels/change-batch-coordinates/{canvas.Name}", requestDto, ct);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to paint coordinate batch on {canvas.Name}: {response.StatusCode}");
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
            Console.WriteLine($"Painting coordinate batch on {canvas.Name} timed out.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error painting coordinate batch: {ex.Message}");
            return null;
        }
    }

    private void RecordBatchAttempt(int pixelCount)
    {
        Interlocked.Increment(ref _batchedRequestCount);
        Interlocked.Add(ref _attemptedPixelCount, pixelCount);
    }

    private static bool TryGetUniformBatch(IReadOnlyCollection<PixelDto> pixels, out int colorId, out long price)
    {
        var firstPixel = pixels.First();
        var firstColorId = firstPixel.ColorId;
        var firstPrice = firstPixel.Price;

        var isUniformBatch = pixels.All(pixel =>
            pixel.Id == null &&
            pixel.OwnerId == null &&
            pixel.ColorId == firstColorId &&
            pixel.Price == firstPrice);

        colorId = firstColorId;
        price = firstPrice;
        return isUniformBatch;
    }

    protected async Task PaintPixelAsync(CanvasDto canvas, int x, int y, int colorId)
    {
        await TryPaintPixelAsync(canvas, x, y, colorId);
    }
}

