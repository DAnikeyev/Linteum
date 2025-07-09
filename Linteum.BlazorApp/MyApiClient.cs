using System.Security.Cryptography;
using System.Text;
using Linteum.BlazorApp.ExtensionMethods;
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
        if (sessionId.HasValue)
        {
            await _localStorage.SetItemAsync(LocalStorageKey.SessionId, sessionId.Value.ToString());
            await _localStorage.SetItemAsync(LocalStorageKey.SessionCreatedAt, DateTime.UtcNow);
        }
        else
        {
            await _localStorage.RemoveItemAsync(LocalStorageKey.SessionId);
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
            await _localStorage.SetItemAsync(LocalStorageKey.LoginMethod, LoginMethod.Password);
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
        await request.AddSessionId(_localStorage);
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

    public async Task ChangeUsernameAsync(string userName)
    {
        if (string.IsNullOrEmpty(userName))
            throw new ArgumentException("Username cannot be empty", nameof(userName));
        var existingEmail = await _localStorage.GetItemAsync<string>(LocalStorageKey.Email);
        if (string.IsNullOrEmpty(existingEmail))
            throw new InvalidOperationException("Email is not set in local storage.");
        var existingLoginMethod = await _localStorage.GetItemAsync<LoginMethod>(LocalStorageKey.LoginMethod);
        var userDto = new UserDto { UserName = userName, Email = existingEmail, LoginMethod = existingLoginMethod };
        var request = new HttpRequestMessage(HttpMethod.Post, "/users/changeName");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(userDto);
        var response = await _httpClient.SendAsync(request);
    
        if (response.IsSuccessStatusCode)
        {
            await _localStorage.SetItemAsync(LocalStorageKey.UserName, userName);
        }
        else
        {
            throw new Exception("Failed to change username.");
        }
    }

    public async Task ChangePasswordAsync(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var existingEmail = await _localStorage.GetItemAsync<string>(LocalStorageKey.Email);
        if (string.IsNullOrEmpty(existingEmail))
            throw new InvalidOperationException("Email is not set in local storage.");
        var existingLoginMethod = await _localStorage.GetItemAsync<LoginMethod>(LocalStorageKey.LoginMethod);

        var passwordHash = HashPassword(password);
        var userDto = new UserDto { Email = existingEmail, LoginMethod = existingLoginMethod };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/users/changePassword?passwordHashOrKey={Uri.EscapeDataString(passwordHash)}&loginMethod={(int)existingLoginMethod}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(userDto);
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Failed to change password.");
        }
    }
}

