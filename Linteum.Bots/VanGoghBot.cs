using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class VanGoghBot : BotBase
{
    private const int BatchSize = 100;

    public VanGoghBot() : base("vangogh@linteum.com", "SecurePassword123!", "VanGoghBot")
    {
    }

    protected override async Task<CanvasDto?> GetOrCreateCanvasAsync()
    {
        try
        {
            return await HttpClient.GetFromJsonAsync<CanvasDto>("Canvases/name/VanGogh");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine("Canvas 'VanGogh' not found, creating...");
            var newCanvas = new CanvasDto
            {
                Name = "VanGogh",
                Width = 100,
                Height = 80, 
                CanvasMode = CanvasMode.FreeDraw
            };
            var response = await HttpClient.PostAsJsonAsync($"Canvases/Add?passwordHash=", newCanvas);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CanvasDto>();
            }
            else
            {
                Console.WriteLine($"Failed to create canvas: {response.StatusCode}");
                return null;
            }
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

        // ImageConverter is in global namespace apparently based on user attachment
        var grid = ImageConverter.ConvertImageToGrid(imagePath, canvas.Width, canvas.Height, colors);
        Console.WriteLine("Image converted to grid.");

        var whiteColor = colors.FirstOrDefault(c => c.HexValue.Normalize().ToUpper() == "#FFFFFF" || c.Name?.ToLower() == "white") 
                         ?? colors.FirstOrDefault(); 
        
        if (whiteColor == null)
        {
             Console.WriteLine("No suitable default color found.");
             return; 
        }

        var random = new Random();
        var batch = new List<PixelDto>(BatchSize);

        Console.WriteLine("Starting painting loop...");
        while (!ct.IsCancellationRequested)
        {
            int x = random.Next(canvas.Width);
            int y = random.Next(canvas.Height);

            ColorDto? targetColor;
            
            if (random.NextDouble() < 0.99)
            {
                targetColor = grid[x, y];
            }
            else
            {
                targetColor = whiteColor;
            }

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
                await Task.Delay(1, ct);
            }
        }
    }
}

