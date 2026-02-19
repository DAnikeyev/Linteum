using System.Net.Http.Json;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public abstract class BotBase
{
    protected readonly HttpClient HttpClient;
    protected readonly string ApiUrl = "https://localhost:7297"; 
    protected string BotEmail { get; }
    protected string BotPassword { get; }
    protected string BotUserName { get; }

    protected BotBase(string email, string password, string userName)
    {
        BotEmail = email;
        BotPassword = password;
        BotUserName = userName;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        HttpClient = new HttpClient(handler) { BaseAddress = new Uri(ApiUrl) };
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

        await RunBehaviorAsync(canvas, colors);
    }

    protected abstract Task<CanvasDto?> GetOrCreateCanvasAsync();
    
    protected abstract Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors);

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
    
    protected async Task PaintPixelAsync(CanvasDto canvas, int x, int y, int colorId)
    {
        var pixelDto = new PixelDto
        {
            X = x, 
            Y = y, 
            ColorId = colorId,
            CanvasId = canvas.Id
        };

        try
        {
            var response = await HttpClient.PostAsJsonAsync($"Pixels/change/{canvas.Name}", pixelDto);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to paint pixel at {x},{y}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error painting pixel: {ex.Message}");
        }
    }
}

