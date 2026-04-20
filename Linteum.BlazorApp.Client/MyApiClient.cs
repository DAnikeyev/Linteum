using System.Net;
using System.Net.Http.Json;
using Linteum.BlazorApp.Client.ExtensionMethods;
using Linteum.BlazorApp.Client.LocalDTO;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.Extensions.Logging;

namespace Linteum.BlazorApp.Client;

public class MyApiClient
{
    private readonly HttpClient _httpClient;
    private readonly LocalStorageService _localStorage;
    private readonly ILogger<MyApiClient> _logger;
    private readonly object _cacheLock = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private readonly Dictionary<Guid, (List<HistoryResponseItem> Data, DateTime Expiry)> _historyCache = new();
    private readonly Dictionary<(string CanvasName, int X, int Y), (PixelDto Data, DateTime Expiry)> _pixelCache = new();
    private List<ColorDto>? _colorsCache;

    public MyApiClient(HttpClient httpClient, LocalStorageService localStorage, ILogger<MyApiClient> logger)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
        _logger = logger;
    }

    public async Task SetSessionAsync(Guid? sessionId)
    {
        ClearAllCaches();
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
        ClearAllCaches();
        _httpClient.DefaultRequestHeaders.Remove(CustomHeaders.SessionId);
    }

    public void InvalidateHistoryCache(Guid pixelId)
    {
        lock (_cacheLock) { _historyCache.Remove(pixelId); }
    }

    public void InvalidatePixelCache(string canvasName, int x, int y)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Data.Id.HasValue)
                _historyCache.Remove(cached.Data.Id.Value);
            _pixelCache.Remove(cacheKey);
        }
    }

    public void ClearCanvasCache(string canvasName)
    {
        var normalizedCanvasName = NormalizeCanvasName(canvasName);
        lock (_cacheLock)
        {
            var keysToRemove = _pixelCache.Keys
                .Where(key => string.Equals(key.CanvasName, normalizedCanvasName, StringComparison.Ordinal))
                .ToList();
            foreach (var key in keysToRemove)
            {
                if (_pixelCache.TryGetValue(key, out var cached) && cached.Data.Id.HasValue)
                    _historyCache.Remove(cached.Data.Id.Value);
                _pixelCache.Remove(key);
            }
        }
    }

    public void HandlePixelColorChanged(string canvasName, int x, int y, int colorId, Guid? pixelId = null)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            Guid? historyPixelId = pixelId;
            if (_pixelCache.TryGetValue(cacheKey, out var cached))
            {
                var updatedPixel = ClonePixel(cached.Data);
                updatedPixel.ColorId = colorId;
                _pixelCache[cacheKey] = (updatedPixel, DateTime.UtcNow.Add(CacheDuration));
                historyPixelId ??= updatedPixel.Id;
            }
            if (historyPixelId.HasValue)
                _historyCache.Remove(historyPixelId.Value);
        }
    }

    public async Task<List<ColorDto>?> GetColorsAsync()
    {
        _logger.LogInformation("GetColorsAsync called");
        if (_colorsCache is not null)
            return new List<ColorDto>(_colorsCache);

        var colors = await _httpClient.GetFromJsonAsync<List<ColorDto>>("/colors");
        if (colors is null) return null;

        _colorsCache = new List<ColorDto>(colors);
        return new List<ColorDto>(_colorsCache);
    }

    public async Task<CanvasDto?> AddCanvasAsync(CanvasDto canvasDto, string? password)
    {
        _logger.LogInformation("AddCanvasAsync called with canvas name: {CanvasName}", canvasDto.Name);
        var passwordHash = string.IsNullOrEmpty(password) ? null : SecurityHelper.HashPassword(password);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/canvases/Add?passwordHash={Uri.EscapeDataString(passwordHash ?? string.Empty)}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(canvasDto);
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync() ?? "No additional error information.";
            _logger.LogError("Failed to add canvas {CanvasName}. Status: {StatusCode}, Error: {Error}", canvasDto.Name, response.StatusCode, errorContent);
            throw new Exception($"Failed to add canvas. {errorContent}");
        }
        _logger.LogInformation("Canvas {CanvasName} added successfully", canvasDto.Name);
        return await response.Content.ReadFromJsonAsync<CanvasDto>();
    }

    public async Task<List<HistoryResponseItem>> GetHistoryAsync(Guid pixelId, bool useCache = false)
    {
        _logger.LogInformation("GetHistoryAsync called with pixelId: {PixelId}, useCache: {UseCache}", pixelId, useCache);
        if (useCache)
        {
            lock (_cacheLock)
            {
                if (_historyCache.TryGetValue(pixelId, out var cached) && cached.Expiry > DateTime.UtcNow)
                    return CloneHistory(cached.Data);
            }
        }
        var request = new HttpRequestMessage(HttpMethod.Get, $"/pixelchangedevents/pixel/{pixelId}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<List<HistoryResponseItem>>();
        var result = history ?? new List<HistoryResponseItem>();
        lock (_cacheLock)
        {
            _historyCache[pixelId] = (CloneHistory(result), DateTime.UtcNow.Add(CacheDuration));
        }
        return result;
    }

    public async Task<(UserDto? User, Guid? SessionId)> LoginAsync(string email, string password)
    {
        _logger.LogInformation("LoginAsync called with email: {Email}, HttpClient.BaseAddress: {BaseAddress}", email, _httpClient.BaseAddress);
        var passwordHash = SecurityHelper.HashPassword(password);
        var userDto = new UserDto { Email = email, LoginMethod = LoginMethod.Password };
        var response = await _httpClient.PostAsJsonAsync($"/users/login?passwordHashOrKey={Uri.EscapeDataString(passwordHash)}", userDto);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Login failed for email: {Email}, status: {StatusCode}", email, response.StatusCode);
            return (null, null);
        }
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            var loggedInUser = loginResponse.User;
            await _localStorage.SetItemAsync(LocalStorageKey.UserName, loggedInUser.UserName);
            await _localStorage.SetItemAsync(LocalStorageKey.Email, loggedInUser.Email);
            await _localStorage.SetItemAsync(LocalStorageKey.LoginMethod, loggedInUser.LoginMethod);
            await SetSessionAsync(loginResponse.SessionId);
            _logger.LogInformation("Login successful for email: {Email}", email);
        }
        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<(UserDto? User, Guid? SessionId, string? Error)> LoginWithGoogleCodeAsync(string code)
    {
        _logger.LogInformation("LoginWithGoogleCodeAsync called.");
        var response = await _httpClient.PostAsJsonAsync("/users/login-google-code", new GoogleLoginCodeRequestDto { Code = code });
        if (!response.IsSuccessStatusCode)
        {
            var error = ParseErrorMessage(await response.Content.ReadAsStringAsync(), "Google login failed.");
            _logger.LogWarning("Google login failed, status: {StatusCode}, error: {Error}", response.StatusCode, error);
            return (null, null, error);
        }
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            var googleUser = loginResponse.User;
            await _localStorage.SetItemAsync(LocalStorageKey.UserName, googleUser.UserName);
            await _localStorage.SetItemAsync(LocalStorageKey.Email, googleUser.Email);
            await _localStorage.SetItemAsync(LocalStorageKey.LoginMethod, googleUser.LoginMethod);
            await SetSessionAsync(loginResponse.SessionId);
            return (googleUser, loginResponse.SessionId, null);
        }
        return (null, null, "Google login failed.");
    }

    public async Task<(UserDto? User, Guid? SessionId)> LoginAsync(Guid sessionId)
    {
        _logger.LogInformation("LoginAsync called with sessionId: {SessionId}", sessionId);
        var response = await _httpClient.PostAsJsonAsync("/users/validate", sessionId);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Session validation failed for sessionId: {SessionId}, status: {StatusCode}", sessionId, response.StatusCode);
            return (null, null);
        }
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            var validatedUser = loginResponse.User;
            await _localStorage.SetItemAsync(LocalStorageKey.UserName, validatedUser.UserName);
            await _localStorage.SetItemAsync(LocalStorageKey.Email, validatedUser.Email);
            await _localStorage.SetItemAsync(LocalStorageKey.LoginMethod, validatedUser.LoginMethod);
            await SetSessionAsync(loginResponse.SessionId);
        }
        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<(UserDto? User, Guid? SessionId)> SignupAsync(string email, string password, string userName)
    {
        _logger.LogInformation("SignupAsync called with email: {Email}", email);
        var passwordHash = SecurityHelper.HashPassword(password);
        var userDto = new UserDto { Email = email, UserName = userName, LoginMethod = LoginMethod.Password };
        var response = await _httpClient.PostAsJsonAsync($"/users/add?passwordHashOrKey={Uri.EscapeDataString(passwordHash)}&loginMethod={(int)LoginMethod.Password}", userDto);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Signup failed for email: {Email}, status: {StatusCode}", email, response.StatusCode);
            return (null, null);
        }
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            var signedUpUser = loginResponse.User;
            await _localStorage.SetItemAsync(LocalStorageKey.UserName, signedUpUser.UserName);
            await _localStorage.SetItemAsync(LocalStorageKey.Email, signedUpUser.Email);
            await _localStorage.SetItemAsync(LocalStorageKey.LoginMethod, signedUpUser.LoginMethod);
            await SetSessionAsync(loginResponse.SessionId);
        }
        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<bool> SubscribeAsync(string canvasName, string? password)
    {
        _logger.LogInformation("SubscribeAsync called with canvasName: {CanvasName}", canvasName);
        var passwordHash = string.IsNullOrEmpty(password) ? null : SecurityHelper.HashPassword(password);
        var requestDto = new SubscribeCanvasRequestDto
        {
            Canvas = new CanvasDto { Name = canvasName },
            Password = new CanvasPasswordDto { PasswordHash = passwordHash }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/canvases/subscribe");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(requestDto);
        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode) return true;
        _logger.LogWarning("Failed to subscribe to canvas: {CanvasName}, Status: {StatusCode}", canvasName, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable) throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new Exception("Cannot subscribe to canvas");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new Exception("Canvas is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to subscribe to {canvasName}. This exception is unexpected.");
    }

    public async Task<List<CanvasDto>> SearchCanvasesAsync(string name)
    {
        _logger.LogInformation("SearchCanvasesAsync called with name: {Name}", name);
        if (string.IsNullOrWhiteSpace(name)) return new List<CanvasDto>();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/canvases/search?name={Uri.EscapeDataString(name)}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
            return canvases ?? new List<CanvasDto>();
        }
        return new List<CanvasDto>();
    }

    public async Task<CanvasDto> GetCanvas(string canvasName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetCanvas called with canvasName: {CanvasName}", canvasName);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/canvases/name/{canvasName}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var canvas = await response.Content.ReadFromJsonAsync<CanvasDto>();
            if (canvas == null) throw new Exception($"Canvas {canvasName} data is null.");
            return canvas;
        }
        if (response.StatusCode == HttpStatusCode.NotFound) throw new Exception($"Canvas {canvasName} is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to get info of {canvasName}. This exception is unexpected.");
    }

    public async Task<bool> UnsubscribeAsync(string canvasName)
    {
        _logger.LogInformation("UnsubscribeAsync called with canvasName: {CanvasName}", canvasName);
        var request = new HttpRequestMessage(HttpMethod.Post, "/canvases/unsubscribe");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(new CanvasDto { Name = canvasName });
        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode) return true;
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable) throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new Exception($"Cannot unsubscribe from canvas {canvasName}");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new Exception("Canvas is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to unsubscribe from {canvasName}. This exception is unexpected.");
    }

    public async Task<List<CanvasDto>> GetSubscribedCanvasesAsync()
    {
        _logger.LogInformation("GetSubscribedCanvasesAsync called");
        var request = new HttpRequestMessage(HttpMethod.Get, "canvases/subscribed");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
        return canvases ?? new List<CanvasDto>();
    }

    public async Task ChangeUsernameAsync(string userName)
    {
        _logger.LogInformation("ChangeUsernameAsync called with userName: {UserName}", userName);
        if (string.IsNullOrEmpty(userName)) throw new ArgumentException("Username cannot be empty", nameof(userName));
        var existingEmail = await _localStorage.GetItemAsync<string>(LocalStorageKey.Email);
        if (string.IsNullOrEmpty(existingEmail)) throw new InvalidOperationException("Email is not set in local storage.");
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
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable) throw new Exception("Service is currently unavailable. Please try again later.");
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("You are not authorized to change the username. Please log in again.");
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new Exception("Failed to change username. User already exists.");
        }
    }

    public async Task ChangePasswordAsync(string password)
    {
        _logger.LogInformation("ChangePasswordAsync called");
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be empty", nameof(password));
        var existingEmail = await _localStorage.GetItemAsync<string>(LocalStorageKey.Email);
        if (string.IsNullOrEmpty(existingEmail)) throw new InvalidOperationException("Email is not set in local storage.");
        var existingLoginMethod = await _localStorage.GetItemAsync<LoginMethod>(LocalStorageKey.LoginMethod);
        var passwordHash = SecurityHelper.HashPassword(password);
        var userDto = new UserDto { Email = existingEmail, LoginMethod = existingLoginMethod };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/users/changePassword?passwordHashOrKey={Uri.EscapeDataString(passwordHash)}&loginMethod={(int)existingLoginMethod}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(userDto);
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new Exception("Failed to change password.");
    }

    public async Task<byte[]> GetCanvasImage(CanvasDto canvasDto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetCanvasImage called with canvasName: {CanvasName}", canvasDto.Name);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/canvases/image/{canvasDto.Name}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new Exception($"Image for canvas {canvasDto.Name} is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to get image of {canvasDto.Name}. This exception is unexpected.");
    }

    public async Task<PixelDto> GetPixelData(string canvasName, int x, int y, bool useCache = false)
    {
        _logger.LogInformation("GetPixelData called with canvasName: {CanvasName}, x: {X}, y: {Y}", canvasName, x, y);
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        if (useCache)
        {
            lock (_cacheLock)
            {
                if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
                    return ClonePixel(cached.Data);
            }
        }
        var request = new HttpRequestMessage(HttpMethod.Get, $"pixels/getpixel/{canvasName}?x={x}&y={y}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var pixel = await response.Content.ReadFromJsonAsync<PixelDto>();
        var result = pixel ?? new PixelDto();
        lock (_cacheLock)
        {
            _pixelCache[cacheKey] = (ClonePixel(result), DateTime.UtcNow.Add(CacheDuration));
        }
        return result;
    }

    public async Task<PixelDto> Paint((int X, int Y) clickedPixel, CanvasDto canvasDto, int colorId, long currentPrice = 0)
    {
        _logger.LogInformation("Paint called with canvasName: {CanvasName}, x: {X}, y: {Y}", canvasDto.Name, clickedPixel.X, clickedPixel.Y);
        var nextPrice = Math.Max(1, currentPrice + 1);
        var pixelDto = new PixelDto { X = clickedPixel.X, Y = clickedPixel.Y, ColorId = colorId, Price = nextPrice, CanvasId = canvasDto.Id };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/pixels/change/{canvasDto.Name}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(pixelDto);
        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var paintedPixel = await response.Content.ReadFromJsonAsync<PixelDto>();
            if (paintedPixel == null) throw new Exception("Painted pixel data is null.");
            var cacheKey = BuildPixelCacheKey(canvasDto.Name, paintedPixel.X, paintedPixel.Y);
            lock (_cacheLock)
            {
                Guid? historyPixelId = paintedPixel.Id;
                if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Data.Id.HasValue)
                    historyPixelId ??= cached.Data.Id.Value;
                _pixelCache[cacheKey] = (ClonePixel(paintedPixel), DateTime.UtcNow.Add(CacheDuration));
                if (historyPixelId.HasValue)
                    _historyCache.Remove(historyPixelId.Value);
            }
            return paintedPixel;
        }
        var errorContent = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable) throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new Exception(ParseErrorMessage(errorContent, "Cannot paint pixel. Possibly insufficient funds."));
        if (response.StatusCode == HttpStatusCode.NotFound) throw new Exception(ParseErrorMessage(errorContent, "Canvas or color is not found."));
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new Exception(ParseErrorMessage(errorContent, "You are not authorized to paint on this canvas."));
        throw new Exception($"Failed to paint pixel at ({pixelDto.X}, {pixelDto.Y}). This exception is unexpected.");
    }

    private static string NormalizeCanvasName(string? canvasName) =>
        (canvasName ?? string.Empty).Trim().ToUpperInvariant();

    private static (string CanvasName, int X, int Y) BuildPixelCacheKey(string canvasName, int x, int y) =>
        (NormalizeCanvasName(canvasName), x, y);

    private static PixelDto ClonePixel(PixelDto pixel) =>
        new() { Id = pixel.Id, X = pixel.X, Y = pixel.Y, ColorId = pixel.ColorId, OwnerId = pixel.OwnerId, Price = pixel.Price, CanvasId = pixel.CanvasId };

    private static List<HistoryResponseItem> CloneHistory(List<HistoryResponseItem> history) =>
        history.Select(item => new HistoryResponseItem
        {
            UserName = item.UserName,
            OldColorId = item.OldColorId,
            NewColorId = item.NewColorId,
            Timestamp = item.Timestamp,
        }).ToList();

    private static string ParseErrorMessage(string? raw, string fallback) =>
        string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim().Trim('"');

    private void ClearAllCaches()
    {
        lock (_cacheLock)
        {
            _historyCache.Clear();
            _pixelCache.Clear();
            _colorsCache = null;
        }
    }
}



