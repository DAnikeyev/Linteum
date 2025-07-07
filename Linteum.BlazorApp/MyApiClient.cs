using System.Security.Cryptography;
using System.Text;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Linteum.BlazorApp;

public class MyApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ProtectedLocalStorage _localStorage;
    private Guid? _sessionId;

    public MyApiClient(HttpClient httpClient, ProtectedLocalStorage localStorage)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
    }

    public async Task SetSessionAsync(Guid? sessionId)
    {
        _sessionId = sessionId;
        _httpClient.DefaultRequestHeaders.Remove("Session-Id");

        if (sessionId.HasValue)
        {
            _httpClient.DefaultRequestHeaders.Add("Session-Id", sessionId.Value.ToString());
            await _localStorage.SetAsync("SessionId", sessionId.Value.ToString());
        }
        else
        {
            await _localStorage.DeleteAsync("SessionId");
        }
    }

    public async Task LoadSessionAsync()
    {
        var result = await _localStorage.GetAsync<string>("SessionId");
        if (result.Success && Guid.TryParse(result.Value, out var sessionId))
        {
            _sessionId = sessionId;
            _httpClient.DefaultRequestHeaders.Remove("Session-Id");
            _httpClient.DefaultRequestHeaders.Add("Session-Id", sessionId.ToString());
        }
    }

    public void ClearSession()
    {
        _sessionId = null;
        _httpClient.DefaultRequestHeaders.Remove("Session-Id");
    }
    
    public async Task<List<ColorDto>?> GetColorsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<ColorDto>>("/colors");
    }

    public async Task<(UserDto? User, Guid? SessionId)> LoginAsync(string email, string password)
    {
        var passwordHash = HashPassword(password);
        var userDto = new UserDto { Email = email, LoginMethod = LoginMethod.Password };
        var response = await _httpClient.PostAsJsonAsync($"/users/login?passwordHashOrKey={Uri.EscapeDataString(passwordHash)}", userDto);
        if (!response.IsSuccessStatusCode)
            return (null, null);

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null)
        {
            await SetSessionAsync(loginResponse.SessionId);
        }
        
        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<bool> SignupAsync(string email, string password, string userName)
    {
        var passwordHash = HashPassword(password);
        var userDto = new UserDto { Email = email, UserName = userName, LoginMethod = LoginMethod.Password };
        var response = await _httpClient.PostAsJsonAsync($"/users/add-or-update?passwordHashOrKey={Uri.EscapeDataString(passwordHash)}&loginMethod={(int)LoginMethod.Password}", userDto);
        return response.IsSuccessStatusCode;
    }
    
    public async Task<List<CanvasDto>> GetSubscribedCanvasesAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "canvases/subscribed");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
        return canvases ?? new List<CanvasDto>();
    }
    
    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }
}

