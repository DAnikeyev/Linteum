using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class CleanerBot : BotBase
{
    private string _targetCanvasName;
    
    public CleanerBot(string targetCanvasName = "Munch") : base("cleaner@linteum.com", "CleanCanvas123!", "CleanerBot")
    {
        _targetCanvasName = targetCanvasName;
    }

    protected override async Task<CanvasDto?> GetOrCreateCanvasAsync()
    {
        // Cleaner just tries to get existing canvas, doesn't create one usually, but for robustness:
        try
        {
            return await HttpClient.GetFromJsonAsync<CanvasDto>($"Canvases/name/{_targetCanvasName}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
           Console.WriteLine($"Canvas '{_targetCanvasName}' not found.");
           return null;
        }
    }

    protected override async Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors)
    {
        var whiteColor = colors.FirstOrDefault(c => c.HexValue.Normalize().ToUpper() == "#FFFFFF" || c.Name?.ToLower() == "white") 
                         ?? colors.FirstOrDefault(); 
        
        if (whiteColor == null)
        {
             Console.WriteLine("No suitable default color found.");
             return; 
        }

        Console.WriteLine("Starting cleaning loop (Left->Right, Top->Bottom)...");
        
        while (true)
        {
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
                {
                    await PaintPixelAsync(canvas, x, y, whiteColor.Id);
                    await Task.Delay(1); 
                }
            }
            Console.WriteLine("Canvas cleared. Restarting cleaning process in 5 seconds...");
            await Task.Delay(5000);
        }
    }
}

