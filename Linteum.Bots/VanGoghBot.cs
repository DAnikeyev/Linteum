using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class VanGoghBot : BotBase
{
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
                CanvasMode = CanvasMode.Sandbox
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

    protected override async Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors)
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

        Console.WriteLine("Starting painting loop...");
        while (true)
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

            await PaintPixelAsync(canvas, x, y, targetColor.Id);

            await Task.Delay(1);
        }
    }
}

