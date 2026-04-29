using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class VanGogh2Bot : BotBase
{
    private const int BatchSize = 100;
    private readonly string CanvasName = "VanGogh";

    public VanGogh2Bot() : base("vangogh2@linteum.com", "SecurePassword123!", "VanGogh2Bot")
    {
    }

    protected override async Task<CanvasDto?> GetOrCreateCanvasAsync()
    {
        try
        {
            return await HttpClient.GetFromJsonAsync<CanvasDto>($"Canvases/name/{CanvasName}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"Canvas '{CanvasName}' not found, creating...");
            var newCanvas = new CanvasDto
            {
                Name = CanvasName,
                Width = 100,
                Height = 80,
                CanvasMode = CanvasMode.FreeDraw
            };

            var response = await HttpClient.PostAsJsonAsync("Canvases/Add?passwordHash=", newCanvas);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CanvasDto>();
            }

            Console.WriteLine($"Failed to create canvas: {response.StatusCode}");
            return null;
        }
    }

    protected override async Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors, CancellationToken ct)
    {
        string imagePath = Path.Combine(AppContext.BaseDirectory, "StarryNight.jpg");
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Image file not found at {imagePath}");
            return;
        }

        var grid = ImageConverter.ConvertImageToGrid(imagePath, canvas.Width, canvas.Height, colors);
        Console.WriteLine("Image converted to grid.");
        Console.WriteLine("Starting continuous batched painting loop...");
        var batch = new List<PixelDto>(BatchSize);

        while (!ct.IsCancellationRequested)
        {
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
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
                        await TryPaintPixelsAsync(canvas, batch, ct);
                        batch.Clear();
                        await Task.Delay(10, ct);
                    }
                }
            }

            if (batch.Count > 0)
            {
                await TryPaintPixelsAsync(canvas, batch, ct);
                batch.Clear();
            }
        }
    }
}
