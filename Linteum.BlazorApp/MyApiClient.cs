using System.Security.Cryptography;
using System.Text;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp;

public class MyApiClient
{
    private readonly HttpClient _httpClient;
    private readonly LocalStorageService _localStorage;

    public MyApiClient(HttpClient httpClient, LocalStorageService localStorage)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
    }

    public async Task SetSessionAsync(Guid? sessionId)
    {
        _httpClient.DefaultRequestHeaders.Remove(CustomHeaders.SessionId);

        if (sessionId.HasValue)
        {
            _httpClient.DefaultRequestHeaders.Add(CustomHeaders.SessionId, sessionId.Value.ToString());
            await _localStorage.SetItemAsync(LocalStorageKey.SessionId, sessionId.Value.ToString());
            await _localStorage.SetItemAsync(LocalStorageKey.SessionCreatedAt, DateTime.UtcNow);
        }
        else
        {
            await _localStorage.RemoveItemAsync(LocalStorageKey.SessionId);
        }
    }

    public async Task LoadSessionAsync()
    {
        var result = await _localStorage.GetItemAsync<string>(LocalStorageKey.SessionId);
        if (result != null && Guid.TryParse(result, out var sessionId))
        {
            _httpClient.DefaultRequestHeaders.Remove(CustomHeaders.SessionId);
            _httpClient.DefaultRequestHeaders.Add(CustomHeaders.SessionId, sessionId.ToString());
        }
    }

    public void ClearSession()
    {
        _httpClient.DefaultRequestHeaders.Remove(CustomHeaders.SessionId);
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
        if (loginResponse?.SessionId != null && loginResponse?.User != null && loginResponse?.User?.UserName != null && loginResponse?.User?.Email != null)
        {
            await _localStorage.SetItemAsync(LocalStorageKey.UserName, loginResponse.User?.UserName);
            await _localStorage.SetItemAsync(LocalStorageKey.Email, loginResponse.User?.Email);
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

