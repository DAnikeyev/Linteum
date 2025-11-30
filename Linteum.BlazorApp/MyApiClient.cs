using System.Net;
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
    
    public async Task<CanvasDto?> AddCanvasAsync(CanvasDto canvasDto, string? password)
    {
        var passwordHash = string.IsNullOrEmpty(password) ? null : SecurityHelper.HashPassword(password);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/canvases/Add?passwordHash={Uri.EscapeDataString(passwordHash ?? string.Empty)}");
    
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(canvasDto);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync() ?? "No additional error information.";
            throw new Exception($"Failed to add canvas. {errorContent}");
        }

        return await response.Content.ReadFromJsonAsync<CanvasDto>();
    }

    public async Task<(UserDto? User, Guid? SessionId)> LoginAsync(string email, string password)
    {
        var passwordHash = SecurityHelper.HashPassword(password);
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

    public async Task<(UserDto? User, Guid? SessionId)> SignupAsync(string email, string password, string userName)
    {
        var passwordHash = SecurityHelper.HashPassword(password);
        var userDto = new UserDto { Email = email, UserName = userName, LoginMethod = LoginMethod.Password };
        var response = await _httpClient.PostAsJsonAsync($"/users/add?passwordHashOrKey={Uri.EscapeDataString(passwordHash)}&loginMethod={(int)LoginMethod.Password}", userDto);
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

    public async Task<bool> SubscribeAsync(string canvasName, string? password)
    {
        var passwordHash = string.IsNullOrEmpty(password) ? null : SecurityHelper.HashPassword(password);
        var canvasDto = new CanvasDto { Name = canvasName };
        var passwordDto = new CanvasPasswordDto { PasswordHash = passwordHash };
        var requestDto = new SubscribeCanvasRequestDto
        {
            Canvas = canvasDto,
            Password = passwordDto
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/canvases/subscribe");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(requestDto);
        var response = await _httpClient.SendAsync(request);
    
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if(response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if(response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception("Cannot subscribe to canvas");
        if(response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas is not found.");
        if(response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to subscribe to {canvasName}. This exception is unexpected.");
    }

    public async Task<bool> UnsubscribeAsync(string canvasName)
    {
        var canvasDto = new CanvasDto { Name = canvasName };
        var request = new HttpRequestMessage(HttpMethod.Post, "/canvases/unsubscribe");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(canvasDto);
        var response = await _httpClient.SendAsync(request);
    
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if(response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if(response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception($"Cannot unsubscribe from canvas {canvasName}");
        if(response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas is not found.");
        if(response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to unsubscribe from {canvasName}. This exception is unexpected.");
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
            if(response.StatusCode == HttpStatusCode.ServiceUnavailable)
                throw new Exception("Service is currently unavailable. Please try again later.");
            if(response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("You are not authorized to change the username. Please log in again.");
            if(response.StatusCode == HttpStatusCode.BadRequest)
                throw new Exception("Failed to change username. User already exists.");
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

        var passwordHash = SecurityHelper.HashPassword(password);
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

