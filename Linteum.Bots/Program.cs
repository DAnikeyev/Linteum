using System.Net.Http.Json;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

class Program
{
    // Update this to match your running API port (check launchSettings.json in the Api project)
    private const string BaseUrl = "https://localhost:7001"; 
    
    // You need a valid Session ID GUID here. 
    // In a real scenario, you might login first to get this, or use a fixed API key if your logic supports it.
    private const string BotSessionId = "d3b07384-d9a1-4d3b-92d7-74a583210123"; 

    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Linteum Bot...");

        using var client = new HttpClient();
        client.BaseAddress = new Uri(BaseUrl);
        
        // The CanvasesController checks for this specific header for authorization
        client.DefaultRequestHeaders.Add("Session-Id", BotSessionId);

        try
        {
            await GetCanvasesAsync(client);
            
            // Example: Attempt to get a specific canvas image
            // await GetCanvasImageAsync(client, "DefaultCanvas");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }

        Console.WriteLine("Bot finished work. Press any key to exit.");
        Console.ReadKey();
    }

    private static async Task GetCanvasesAsync(HttpClient client)
    {
        Console.WriteLine("Fetching all canvases...");

        // Corresponds to [HttpGet] in CanvasesController
        var response = await client.GetAsync("Canvases?includePrivate=true");

        if (response.IsSuccessStatusCode)
        {
            var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
            if (canvases != null)
            {
                foreach (var canvas in canvases)
                {
                    Console.WriteLine($"Found Canvas: {canvas.Name} ({canvas.Width}x{canvas.Height}) - ID: {canvas.Id}");
                }
            }
        }
        else
        {
            Console.WriteLine($"Failed to fetch canvases. Status: {response.StatusCode}");
            string content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response: {content}");
        }
    }

    private static async Task GetCanvasImageAsync(HttpClient client, string canvasName)
    {
        Console.WriteLine($"Downloading image for {canvasName}...");
        
        // Corresponds to [HttpGet("image/{name}")]
        var response = await client.GetAsync($"Canvases/image/{canvasName}");

        if (response.IsSuccessStatusCode)
        {
            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync($"{canvasName}.png", imageBytes);
            Console.WriteLine($"Image saved to {canvasName}.png");
        }
        else
        {
            Console.WriteLine($"Failed to get image. Status: {response.StatusCode}");
        }
    }
}
