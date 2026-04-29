using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;
using SixLabors.ImageSharp;

namespace Linteum.Bots;

public class XeroxBot : BotBase
{
    private const int BatchSize = 100;
    private const int MaxRetries = 5;
    private const int RequestDelayMs = 1;

    private readonly string _canvasName;
    private readonly string _imageName;

    public XeroxBot(string canvasName, string imageName)
        : base("xerox@linteum.com", "XeroxCopy123!", "XeroxBot")
    {
        _canvasName = canvasName;
        _imageName = imageName;
    }

    private string GetImagePath()
    {
        // Try the image name as-is (absolute or relative), then fall back to base directory
        if (File.Exists(_imageName))
            return Path.GetFullPath(_imageName);

        var basePath = Path.Combine(AppContext.BaseDirectory, _imageName);
        if (File.Exists(basePath))
            return basePath;

        return _imageName; // let caller handle missing file
    }

    protected override async Task<CanvasDto?> GetOrCreateCanvasAsync()
    {
        using var response = await HttpClient.GetAsync($"Canvases/name/{Uri.EscapeDataString(_canvasName)}");
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<CanvasDto>();
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"Canvas '{_canvasName}' not found, creating with image dimensions...");

            var imagePath = GetImagePath();
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"Image file not found: {imagePath}");
                return null;
            }

            var imageInfo = Image.Identify(imagePath);

            var newCanvas = new CanvasDto
            {
                Name = _canvasName,
                Width = imageInfo.Width,
                Height = imageInfo.Height,
                CanvasMode = CanvasMode.FreeDraw
            };

            Console.WriteLine($"Creating canvas '{_canvasName}' ({newCanvas.Width}x{newCanvas.Height})...");
            var createResponse = await HttpClient.PostAsJsonAsync("Canvases/Add?passwordHash=", newCanvas);
            if (createResponse.IsSuccessStatusCode)
            {
                return await createResponse.Content.ReadFromJsonAsync<CanvasDto>();
            }

            Console.WriteLine($"Failed to create canvas: {createResponse.StatusCode}");
            return null;
        }

        var errorBody = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Failed to fetch canvas '{_canvasName}': {(int)response.StatusCode} {response.StatusCode}");
        if (!string.IsNullOrWhiteSpace(errorBody))
            Console.WriteLine(errorBody);

        return null;
    }

    protected override async Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors, CancellationToken ct)
    {
        var imagePath = GetImagePath();
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Image file not found: {imagePath}");
            
            return;
        }

        Console.WriteLine($"Converting image to {canvas.Width}x{canvas.Height} grid...");
        var grid = ImageConverter.ConvertImageToGrid(imagePath, canvas.Width, canvas.Height, colors);
        Console.WriteLine("Image converted to grid.");

        // Build a list of all pixel coordinates and shuffle them
        var pixels = new List<(int X, int Y)>(canvas.Width * canvas.Height);
        for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
                pixels.Add((x, y));

        var random = new Random();
        // Fisher-Yates shuffle
        for (int i = pixels.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (pixels[i], pixels[j]) = (pixels[j], pixels[i]);
        }

        Console.WriteLine($"Drawing {pixels.Count} pixels in random order in batches of {BatchSize}...");

        int drawn = 0;
        int failed = 0;
        var batch = new List<PixelDto>(BatchSize);

        foreach (var (x, y) in pixels)
        {
            var targetColor = grid[x, y];
            batch.Add(new PixelDto
            {
                X = x,
                Y = y,
                ColorId = targetColor.Id,
                CanvasId = canvas.Id,
            });

            if (batch.Count >= BatchSize)
            {
                var changedCount = await PaintPixelBatchWithRetriesAsync(canvas, batch, ct);
                drawn += changedCount;
                failed += batch.Count - changedCount;
                if (drawn > 0 && drawn % 1000 < BatchSize)
                {
                    Console.WriteLine($"Progress: {drawn}/{pixels.Count} pixels drawn.");
                }

                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            var changedCount = await PaintPixelBatchWithRetriesAsync(canvas, batch, ct);
            drawn += changedCount;
            failed += batch.Count - changedCount;
        }

        Console.WriteLine($"Done! Drawn {drawn}/{pixels.Count} pixels. Failed: {failed}.");
    }

    private async Task<int> PaintPixelBatchWithRetriesAsync(CanvasDto canvas, IReadOnlyCollection<PixelDto> pixels, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            var result = await TryPaintPixelsAsync(canvas, pixels, ct);
            await Task.Delay(RequestDelayMs, ct);

            if (result != null)
                return result.ChangedPixels.Count;
        }

        Console.WriteLine($"Failed to draw a pixel batch after {MaxRetries + 1} attempts.");
        return 0;
    }
}


