using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Linteum.BlazorApp.ExtensionMethods;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp;

internal class MyApiClient
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
            await _localStorage.RemoveItemAsync(LocalStorageKey.UserId);
        }
    }

    public void ClearSession()
    {
        ClearAllCaches();
        _httpClient.DefaultRequestHeaders.Remove(CustomHeaders.SessionId);
    }

    public void InvalidateHistoryCache(Guid pixelId)
    {
        lock (_cacheLock)
        {
            _historyCache.Remove(pixelId);
        }
    }

    public void InvalidatePixelCache(string canvasName, int x, int y)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Data.Id.HasValue)
            {
                _historyCache.Remove(cached.Data.Id.Value);
            }
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
                {
                    _historyCache.Remove(cached.Data.Id.Value);
                }
                _pixelCache.Remove(key);
            }
        }
    }

    public void HandlePixelColorChanged(string canvasName, int x, int y, int colorId, Guid? pixelId = null, Guid? ownerId = null)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            Guid? historyPixelId = pixelId;
            var updatedPixel = _pixelCache.TryGetValue(cacheKey, out var cached)
                ? ClonePixel(cached.Data)
                : new PixelDto
                {
                    X = x,
                    Y = y,
                };

            updatedPixel.ColorId = colorId;
            updatedPixel.OwnerId = ownerId;
            updatedPixel.Id = pixelId ?? updatedPixel.Id;
            _pixelCache[cacheKey] = (updatedPixel, DateTime.UtcNow.Add(CacheDuration));
            historyPixelId ??= updatedPixel.Id;

            if (historyPixelId.HasValue)
            {
                _historyCache.Remove(historyPixelId.Value);
            }
        }
    }

    public int? GetWhiteColorId()
    {
        lock (_cacheLock)
        {
            return ResolveWhiteColorId();
        }
    }

    public bool IsPixelKnownWhite(string canvasName, int x, int y)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            return _pixelCache.TryGetValue(cacheKey, out var cached)
                && cached.Expiry > DateTime.UtcNow
                && IsWhitePixel(cached.Data);
        }
    }

    public void HandlePixelDeleted(string canvasName, int x, int y, Guid canvasId)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            var whiteColorId = ResolveWhiteColorId();
            if (!whiteColorId.HasValue)
            {
                if (_pixelCache.TryGetValue(cacheKey, out var cachedWithoutWhiteId) && cachedWithoutWhiteId.Data.Id.HasValue)
                {
                    _historyCache.Remove(cachedWithoutWhiteId.Data.Id.Value);
                }

                _pixelCache.Remove(cacheKey);
                return;
            }

            if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Data.Id.HasValue)
            {
                _historyCache.Remove(cached.Data.Id.Value);
            }

            _pixelCache[cacheKey] = (new PixelDto
            {
                X = x,
                Y = y,
                ColorId = whiteColorId.Value,
                CanvasId = canvasId,
                Id = null,
                OwnerId = null,
                Price = 0,
            }, DateTime.UtcNow.Add(CacheDuration));
        }
    }

    public async Task<Guid?> GetCurrentUserIdAsync()
    {
        return await _localStorage.GetItemAsync<Guid?>(LocalStorageKey.UserId);
    }
    
    public async Task<List<ColorDto>?> GetColorsAsync()
    {
        _logger.LogInformation("GetColorsAsync called");
        if (_colorsCache is not null)
        {
            return new List<ColorDto>(_colorsCache);
        }

        var colors = await _httpClient.GetFromJsonAsync<List<ColorDto>>("/colors");
        if (colors is null)
        {
            return null;
        }

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

    public async Task<CanvasDto?> AddCanvasFromImageAsync(CanvasDto canvasDto, string? password, byte[] imageBytes, string fileName)
    {
        _logger.LogInformation("AddCanvasFromImageAsync called with canvas name: {CanvasName}", canvasDto.Name);

        var passwordHash = string.IsNullOrEmpty(password) ? null : SecurityHelper.HashPassword(password);
        using var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(new StringContent(canvasDto.Name), nameof(canvasDto.Name));
        multipartContent.Add(new StringContent(((int)canvasDto.CanvasMode).ToString(CultureInfo.InvariantCulture)), nameof(canvasDto.CanvasMode));

        if (!string.IsNullOrEmpty(passwordHash))
        {
            multipartContent.Add(new StringContent(passwordHash), "PasswordHash");
        }

        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipartContent.Add(imageContent, "Image", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/canvases/add-with-image");
        await request.AddSessionId(_localStorage);
        request.Content = multipartContent;

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync() ?? "No additional error information.";
            _logger.LogError("Failed to add canvas from image {CanvasName}. Status: {StatusCode}, Error: {Error}", canvasDto.Name, response.StatusCode, errorContent);
            throw new Exception($"Failed to add canvas from image. {ParseErrorMessage(errorContent, "No additional error information.")}");
        }

        _logger.LogInformation("Canvas {CanvasName} added from image successfully", canvasDto.Name);
        return await response.Content.ReadFromJsonAsync<CanvasDto>();
    }

    public async Task SendCanvasChatMessageAsync(string canvasName, string message)
    {
        if (string.IsNullOrWhiteSpace(canvasName))
            throw new ArgumentException("Canvas name is required.", nameof(canvasName));

        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
            throw new ArgumentException("Message is required.", nameof(message));

        if (normalizedMessage.Length > SendCanvasChatMessageRequestDto.MaxMessageLength)
            throw new ArgumentException($"Message must be {SendCanvasChatMessageRequestDto.MaxMessageLength} characters or less.", nameof(message));

        var request = new HttpRequestMessage(HttpMethod.Post, $"/canvaschat/{Uri.EscapeDataString(canvasName)}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(new SendCanvasChatMessageRequestDto
        {
            Message = normalizedMessage,
        });

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning(
            "Failed to send canvas chat message to {CanvasName}. Status: {StatusCode}, Error: {Error}",
            canvasName,
            response.StatusCode,
            errorContent);

        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ParseErrorMessage(errorContent, "Cannot send chat message."));
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("You are not authorized to send chat messages on this canvas.");

        throw new Exception(ParseErrorMessage(errorContent, $"Failed to send a chat message to {canvasName}."));
    }

    public async Task<NormalModeQuotaDto?> GetNormalModeQuotaAsync(string canvasName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/pixels/quota/{Uri.EscapeDataString(canvasName)}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NormalModeQuotaDto>();
    }

    public async Task<List<HistoryResponseItem>> GetHistoryAsync(Guid pixelId, bool useCache = false)
    {
        _logger.LogInformation("GetHistoryAsync called with pixelId: {PixelId}, useCache: {UseCache}", pixelId, useCache);
        if (useCache)
        {
            lock (_cacheLock)
            {
                if (_historyCache.TryGetValue(pixelId, out var cached) && cached.Expiry > DateTime.UtcNow)
                {
                    return CloneHistory(cached.Data);
                }
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
        _logger.LogInformation("LoginAsync called with email: {Email}", email);
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
            await PersistAuthenticatedUserAsync(loginResponse.User, loginResponse.SessionId.Value);
            _logger.LogInformation("Login successful for email: {Email}", email);
        }
        else
        {
            _logger.LogWarning("Login response data missing for email: {Email}", email);
        }
        
        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<(UserDto? User, Guid? SessionId, string? Error)> LoginWithGoogleCodeAsync(string code)
    {
        _logger.LogInformation("LoginWithGoogleCodeAsync called.");
        var response = await _httpClient.PostAsJsonAsync(
            "/users/login-google-code",
            new GoogleLoginCodeRequestDto { Code = code });

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
            await PersistAuthenticatedUserAsync(googleUser, loginResponse.SessionId.Value);
            _logger.LogInformation("Google login successful for email: {Email}", googleUser.Email);
            return (googleUser, loginResponse.SessionId, null);
        }

        _logger.LogWarning("Google login response data missing.");
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
            await PersistAuthenticatedUserAsync(loginResponse.User, loginResponse.SessionId.Value);
            _logger.LogInformation("Session validation successful for sessionId: {SessionId}", sessionId);
        }
        else
        {
            _logger.LogWarning("Session validation response data missing for sessionId: {SessionId}", sessionId);
        }

        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<(UserDto? User, Guid? SessionId)> SignupAsync(string email, string password, string userName)
    {
        _logger.LogInformation("SignupAsync called with email: {Email}, userName: {UserName}", email, userName);
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
            await PersistAuthenticatedUserAsync(loginResponse.User, loginResponse.SessionId.Value);
            _logger.LogInformation("Signup successful for email: {Email}", email);
        }
        else
        {
            _logger.LogWarning("Signup response data missing for email: {Email}", email);
        }
        
        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<bool> SubscribeAsync(string canvasName, string? password)
    {
        _logger.LogInformation("SubscribeAsync called with canvasName: {CanvasName}", canvasName);
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
            _logger.LogInformation("Successfully subscribed to canvas: {CanvasName}", canvasName);
            return true;
        }

        _logger.LogWarning("Failed to subscribe to canvas: {CanvasName}, Status: {StatusCode}", canvasName, response.StatusCode);
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

    public async Task<List<CanvasDto>> SearchCanvasesAsync(string name)
    {
        _logger.LogInformation("SearchCanvasesAsync called with name: {Name}", name);
        if (string.IsNullOrWhiteSpace(name))
            return new List<CanvasDto>();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/canvases/search?name={Uri.EscapeDataString(name)}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
        
        if (response.IsSuccessStatusCode)
        {
            var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
            return canvases ?? new List<CanvasDto>();
        }

        _logger.LogWarning("Failed to search canvases with name: {Name}, Status: {StatusCode}", name, response.StatusCode);
        return new List<CanvasDto>();
    }
    
    public async Task<CanvasDto> GetCanvas(string canvasName)
    {
        _logger.LogInformation("GetCanvas called with canvasName: {CanvasName}", canvasName);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/canvases/name/{canvasName}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
    
        if (response.IsSuccessStatusCode)
        {
            var canvas = await response.Content.ReadFromJsonAsync<CanvasDto>();
            if (canvas == null)
            {
                _logger.LogError("Canvas {CanvasName} data is null in successful response", canvasName);
                throw new Exception($"Canvas {canvasName} data is null.");
            }
            return canvas;
        }

        _logger.LogWarning("Failed to get canvas {CanvasName}, Status: {StatusCode}", canvasName, response.StatusCode);
        if(response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception($"Canvas {canvasName} is not found.");
        if(response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to get info of {canvasName}. This exception is unexpected.");
    }

    public async Task<bool> UnsubscribeAsync(string canvasName)
    {
        _logger.LogInformation("UnsubscribeAsync called with canvasName: {CanvasName}", canvasName);
        var canvasDto = new CanvasDto { Name = canvasName };
        var request = new HttpRequestMessage(HttpMethod.Post, "/canvases/unsubscribe");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(canvasDto);
        var response = await _httpClient.SendAsync(request);
    
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully unsubscribed from canvas: {CanvasName}", canvasName);
            return true;
        }

        _logger.LogWarning("Failed to unsubscribe from canvas {CanvasName}, Status: {StatusCode}", canvasName, response.StatusCode);
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
        _logger.LogInformation("GetSubscribedCanvasesAsync called");
        var request = new HttpRequestMessage(HttpMethod.Get, "canvases/subscribed");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
        return canvases ?? new List<CanvasDto>();
    }

    public async Task<CanvasOperationResponseDto> EraseCanvasAsync(string canvasName)
    {
        _logger.LogInformation("EraseCanvasAsync called with canvasName: {CanvasName}", canvasName);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/canvases/erase/{Uri.EscapeDataString(canvasName)}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<CanvasOperationResponseDto>()
                ?? new CanvasOperationResponseDto
                {
                    Completed = response.StatusCode == HttpStatusCode.OK,
                    Queued = response.StatusCode == HttpStatusCode.Accepted,
                    Message = response.StatusCode == HttpStatusCode.Accepted
                        ? $"Canvas {canvasName} erase was queued."
                        : $"Canvas {canvasName} was erased.",
                };

            if (result.Completed)
            {
                ClearCanvasCache(canvasName);
            }

            _logger.LogInformation(
                "Canvas {CanvasName} erase request succeeded. Completed={Completed}, Queued={Queued}, StatusCode={StatusCode}, Message={Message}",
                canvasName,
                result.Completed,
                result.Queued,
                response.StatusCode,
                result.Message);
            return result;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to erase canvas {CanvasName}, Status: {StatusCode}, Error: {Error}", canvasName, response.StatusCode, errorContent);
        throw CreateCanvasManagementException(response.StatusCode, ParseErrorMessage(errorContent, $"Failed to erase canvas {canvasName}."), "erase");
    }

    public async Task<CanvasOperationResponseDto> DeleteCanvasAsync(string canvasName)
    {
        _logger.LogInformation("DeleteCanvasAsync called with canvasName: {CanvasName}", canvasName);
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/canvases/delete/{Uri.EscapeDataString(canvasName)}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<CanvasOperationResponseDto>()
                ?? new CanvasOperationResponseDto
                {
                    Completed = response.StatusCode == HttpStatusCode.OK,
                    Queued = response.StatusCode == HttpStatusCode.Accepted,
                    Message = response.StatusCode == HttpStatusCode.Accepted
                        ? $"Canvas {canvasName} deletion was queued."
                        : $"Canvas {canvasName} was deleted.",
                };

            if (result.Completed)
            {
                ClearCanvasCache(canvasName);
            }

            _logger.LogInformation(
                "Canvas {CanvasName} delete request succeeded. Completed={Completed}, Queued={Queued}, StatusCode={StatusCode}, Message={Message}",
                canvasName,
                result.Completed,
                result.Queued,
                response.StatusCode,
                result.Message);
            return result;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to delete canvas {CanvasName}, Status: {StatusCode}, Error: {Error}", canvasName, response.StatusCode, errorContent);
        throw CreateCanvasManagementException(response.StatusCode, ParseErrorMessage(errorContent, $"Failed to delete canvas {canvasName}."), "delete");
    }

    public async Task<long> GetCurrentGoldAsync(Guid canvasId)
    {
        _logger.LogInformation("GetCurrentGoldAsync called with canvasId: {CanvasId}", canvasId);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/balancechangedevents/current/canvas/{canvasId}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<long>();
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to get current gold for canvas {CanvasId}. Status: {StatusCode}, Error: {Error}", canvasId, response.StatusCode, errorContent);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("You are not authorized to view gold for this canvas.");
        throw new Exception("Failed to get current gold.");
    }

    public async Task ChangeUsernameAsync(string userName)
    {
        _logger.LogInformation("ChangeUsernameAsync called with userName: {UserName}", userName);
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
            _logger.LogInformation("Successfully changed username to {UserName}", userName);
            await _localStorage.SetItemAsync(LocalStorageKey.UserName, userName);
        }
        else
        {
            _logger.LogWarning("Failed to change username to {UserName}, Status: {StatusCode}", userName, response.StatusCode);
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
        _logger.LogInformation("ChangePasswordAsync called");
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

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully changed password for email: {Email}", existingEmail);
        }
        else
        {
            _logger.LogWarning("Failed to change password for email: {Email}, Status: {StatusCode}", existingEmail, response.StatusCode);
            throw new Exception("Failed to change password.");
        }
    }

    public async Task<byte[]> GetCanvasImage(CanvasDto canvasDto)
    {
        _logger.LogInformation("GetCanvasImage called with canvasName: {CanvasName}", canvasDto.Name);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/canvases/image/{canvasDto.Name}");
        await request.AddSessionId(_localStorage);
        var response = await _httpClient.SendAsync(request);
    
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsByteArrayAsync();
        }

        if(response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception($"Image for canvas {canvasDto.Name} is not found.");
        if(response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to get image of {canvasDto.Name}. This exception is unexpected.");
    }
    
    public async Task<PixelDto> GetPixelData(string canvasName, int x, int y, bool useCache = false)
    {
        _logger.LogInformation("GetPixelData called with canvasName: {CanvasName}, x: {X}, y: {Y}, useCache: {UseCache}", canvasName, x, y, useCache);
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        if (useCache)
        {
            lock (_cacheLock)
            {
                if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
                {
                    return ClonePixel(cached.Data);
                }
            }
        }

        var pixelDto = new PixelDto
        {
            X = x,
            Y = y,
        };
        var request = new HttpRequestMessage(HttpMethod.Get, $"pixels/getpixel/{canvasName}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(pixelDto);
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
    
    public async Task<PixelDto> Paint((int X, int Y) clickedPixel, CanvasDto canvasDto, int colorId)
    {
        return await Paint(clickedPixel, canvasDto, colorId, price: 0);
    }

    public async Task<PixelDto> Paint((int X, int Y) clickedPixel, CanvasDto canvasDto, int colorId, long price)
    {
        _logger.LogInformation("Paint called with canvasName: {CanvasName}, mode: {CanvasMode}, x: {X}, y: {Y}, colorId: {ColorId}, price: {Price}", canvasDto.Name, canvasDto.CanvasMode, clickedPixel.X, clickedPixel.Y, colorId, price);

        var pixelDto = new PixelDto
        {
            X = clickedPixel.X,
            Y = clickedPixel.Y,
            ColorId = colorId,
            Price = price,
            CanvasId = canvasDto.Id,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/pixels/change/{canvasDto.Name}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(pixelDto);
        var response = await _httpClient.SendAsync(request);
    
        if (response.IsSuccessStatusCode)
        {
            var paintedPixel = await response.Content.ReadFromJsonAsync<PixelDto>();
            if (paintedPixel == null)
            {
                _logger.LogError("Painted pixel data is null for {CanvasName} at ({X}, {Y})", canvasDto.Name, clickedPixel.X, clickedPixel.Y);
                throw new Exception("Painted pixel data is null.");
            }

            var cacheKey = BuildPixelCacheKey(canvasDto.Name, paintedPixel.X, paintedPixel.Y);
            lock (_cacheLock)
            {
                Guid? historyPixelId = paintedPixel.Id;
                if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Data.Id.HasValue)
                {
                    historyPixelId ??= cached.Data.Id.Value;
                }

                _pixelCache[cacheKey] = (ClonePixel(paintedPixel), DateTime.UtcNow.Add(CacheDuration));
                if (historyPixelId.HasValue)
                {
                    _historyCache.Remove(historyPixelId.Value);
                }
            }

            _logger.LogInformation("Successfully painted pixel at ({X}, {Y}) on {CanvasName} with price {Price}", clickedPixel.X, clickedPixel.Y, canvasDto.Name, paintedPixel.Price);
            return paintedPixel;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to paint pixel at ({X}, {Y}) on {CanvasName}, Status: {StatusCode}, Error: {Error}", clickedPixel.X, clickedPixel.Y, canvasDto.Name, response.StatusCode, errorContent);
        if(response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if(response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ParseErrorMessage(errorContent, "Cannot paint pixel. Possibly insufficient funds."));
        if(response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas or color is not found.");
        if(response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("You are not authorized to paint on this canvas.");
        throw new Exception($"Failed to paint pixel at ({pixelDto.X}, {pixelDto.Y}). This exception is unexpected.");
    }

    public async Task<PixelBatchChangeResultDto> PaintBatch(CanvasDto canvasDto, IReadOnlyCollection<PixelDto> pixels, string? masterPassword = null)
    {
        _logger.LogInformation("PaintBatch called with canvasName: {CanvasName}, mode: {CanvasMode}, pixelCount: {PixelCount}", canvasDto.Name, canvasDto.CanvasMode, pixels.Count);

        var requestDto = new PixelBatchChangeRequestDto
        {
            MasterPassword = masterPassword,
            Pixels = pixels.Select(pixel => new PixelDto
            {
                Id = pixel.Id,
                X = pixel.X,
                Y = pixel.Y,
                ColorId = pixel.ColorId,
                OwnerId = pixel.OwnerId,
                Price = pixel.Price,
                CanvasId = canvasDto.Id,
            }).ToList(),
        };

        return await SendPaintBatchAsync(canvasDto, requestDto, $"/pixels/change-batch/{canvasDto.Name}");
    }

    public async Task<PixelBatchChangeResultDto> PaintBatch(CanvasDto canvasDto, IReadOnlyCollection<CoordinateDto> coordinates, int colorId, long price = 0, string? masterPassword = null, StrokePlaybackMetadataDto? playback = null)
    {
        var coordinateList = coordinates.ToList();
        _logger.LogInformation("PaintBatch called with canvasName: {CanvasName}, mode: {CanvasMode}, coordinateCount: {CoordinateCount}, colorId: {ColorId}", canvasDto.Name, canvasDto.CanvasMode, coordinateList.Count, colorId);

        var requestDto = new PixelBatchDto
        {
            MasterPassword = masterPassword,
            ColorId = colorId,
            Price = price,
            Coordinates = coordinateList,
            Playback = playback,
        };

        return await SendPaintBatchAsync(
            canvasDto,
            requestDto,
            $"/pixels/change-batch-coordinates/{canvasDto.Name}",
            onNotFoundFallback: async () =>
            {
                _logger.LogWarning("Coordinate batch paint route was not found for {CanvasName}; falling back to the legacy pixel batch route.", canvasDto.Name);
                return await PaintBatch(
                    canvasDto,
                    coordinateList.Select(coordinate => new PixelDto
                    {
                        X = coordinate.X,
                        Y = coordinate.Y,
                        ColorId = colorId,
                        Price = price,
                        CanvasId = canvasDto.Id,
                    }).ToList(),
                    masterPassword);
            });
    }

    private async Task<PixelBatchChangeResultDto> SendPaintBatchAsync<TRequest>(CanvasDto canvasDto, TRequest requestDto, string requestUri, Func<Task<PixelBatchChangeResultDto>>? onNotFoundFallback = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(requestDto);
        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<PixelBatchChangeResultDto>();
            if (result == null)
            {
                _logger.LogError("PaintBatch returned null result for {CanvasName}", canvasDto.Name);
                throw new Exception("Batch paint result is null.");
            }

            ApplyBatchPaintCache(canvasDto.Name, result.ChangedPixels);
            _logger.LogInformation("Successfully painted batch on {CanvasName}. Requested={RequestedCount}, Successful={SuccessfulCount}", canvasDto.Name, result.RequestedCount, result.ChangedPixels.Count);
            return result;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to paint batch on {CanvasName}, Status: {StatusCode}, Error: {Error}", canvasDto.Name, response.StatusCode, errorContent);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ParseErrorMessage(errorContent, "Cannot paint pixels. Possibly insufficient funds or quota."));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            if (onNotFoundFallback != null)
            {
                return await onNotFoundFallback();
            }

            throw new Exception("Canvas or color is not found.");
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("You are not authorized to paint on this canvas.");
        throw new Exception($"Failed to paint a pixel batch on {canvasDto.Name}. This exception is unexpected.");
    }

    public async Task<PixelBatchDeleteResultDto> DeleteBatchAsync(CanvasDto canvasDto, IReadOnlyCollection<CoordinateDto> coordinates, string? masterPassword = null, StrokePlaybackMetadataDto? playback = null)
    {
        _logger.LogInformation("DeleteBatchAsync called with canvasName: {CanvasName}, coordinateCount: {CoordinateCount}", canvasDto.Name, coordinates.Count);

        var requestDto = new PixelBatchDeleteRequestDto
        {
            MasterPassword = masterPassword,
            Coordinates = coordinates.ToList(),
            Playback = playback,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/pixels/delete-batch/{canvasDto.Name}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(requestDto);
        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<PixelBatchDeleteResultDto>();
            if (result == null)
            {
                _logger.LogError("DeleteBatch returned null result for {CanvasName}", canvasDto.Name);
                throw new Exception("Batch delete result is null.");
            }

            _logger.LogInformation("Successfully deleted batch on {CanvasName}. Requested={RequestedCount}, DeletedCount={DeletedCount}", canvasDto.Name, coordinates.Count, result.DeletedCount);

            foreach (var coord in result.DeletedCoordinates)
            {
                HandlePixelDeleted(canvasDto.Name, coord.X, coord.Y, canvasDto.Id);
            }

            return result;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to delete batch on {CanvasName}, Status: {StatusCode}, Error: {Error}", canvasDto.Name, response.StatusCode, errorContent);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ParseErrorMessage(errorContent, "Cannot delete pixels."));
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("You are not authorized to delete pixels on this canvas.");
        throw new Exception($"Failed to delete a pixel batch on {canvasDto.Name}. This exception is unexpected.");
    }


    public async Task PaintTextAsync((int X, int Y) clickedPixel, CanvasDto canvasDto, string text, int textColorId, int? backgroundColorId, int fontSize)
    {
        _logger.LogInformation(
            "PaintTextAsync called with canvasName: {CanvasName}, x: {X}, y: {Y}, textColorId: {TextColorId}, backgroundColorId: {BackgroundColorId}, fontSize: {FontSize}, textLength: {TextLength}",
            canvasDto.Name,
            clickedPixel.X,
            clickedPixel.Y,
            textColorId,
            backgroundColorId,
            fontSize,
            text.Length);

        var requestDto = new TextDrawRequestDto
        {
            X = clickedPixel.X,
            Y = clickedPixel.Y,
            Text = text,
            FontSize = fontSize.ToString(CultureInfo.InvariantCulture),
            TextColorId = textColorId,
            BackgroundColorId = backgroundColorId,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/pixels/text/{canvasDto.Name}");
        await request.AddSessionId(_localStorage);
        request.SetJsonContent(requestDto);
        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully queued text drawing at ({X}, {Y}) on {CanvasName}", clickedPixel.X, clickedPixel.Y, canvasDto.Name);
            return;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning(
            "Failed to queue text drawing at ({X}, {Y}) on {CanvasName}, Status: {StatusCode}, Error: {Error}",
            clickedPixel.X,
            clickedPixel.Y,
            canvasDto.Name,
            response.StatusCode,
            errorContent);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ParseErrorMessage(errorContent, "Cannot queue text drawing."));
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception(ParseErrorMessage(errorContent, "Canvas or color is not found."));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("You are not authorized to draw text on this canvas.");
        throw new Exception($"Failed to queue text drawing at ({requestDto.X}, {requestDto.Y}). This exception is unexpected.");
    }

    private static string NormalizeCanvasName(string? canvasName) =>
        (canvasName ?? string.Empty).Trim().ToUpperInvariant();

    private static (string CanvasName, int X, int Y) BuildPixelCacheKey(string canvasName, int x, int y) =>
        (NormalizeCanvasName(canvasName), x, y);

    private int? ResolveWhiteColorId() =>
        _colorsCache?.FirstOrDefault(color =>
            string.Equals(color.HexValue, "#FFFFFF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(color.Name, "White", StringComparison.OrdinalIgnoreCase))?.Id;

    private bool IsWhitePixel(PixelDto pixel)
    {
        var whiteColorId = ResolveWhiteColorId();
        return whiteColorId.HasValue && pixel.ColorId == whiteColorId.Value;
    }

    private static PixelDto ClonePixel(PixelDto pixel) =>
        new()
        {
            Id = pixel.Id,
            X = pixel.X,
            Y = pixel.Y,
            ColorId = pixel.ColorId,
            OwnerId = pixel.OwnerId,
            Price = pixel.Price,
            CanvasId = pixel.CanvasId,
        };

    private static List<HistoryResponseItem> CloneHistory(List<HistoryResponseItem> history) =>
        history.Select(item => new HistoryResponseItem
        {
            UserName = item.UserName,
            OldColorId = item.OldColorId,
            NewColorId = item.NewColorId,
            Timestamp = item.Timestamp,
        }).ToList();

    private void ApplyBatchPaintCache(string canvasName, IReadOnlyCollection<PixelDto> changedPixels)
    {
        lock (_cacheLock)
        {
            foreach (var changedPixel in changedPixels)
            {
                var cacheKey = BuildPixelCacheKey(canvasName, changedPixel.X, changedPixel.Y);
                Guid? historyPixelId = changedPixel.Id;
                if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Data.Id.HasValue)
                {
                    historyPixelId ??= cached.Data.Id.Value;
                }

                _pixelCache[cacheKey] = (ClonePixel(changedPixel), DateTime.UtcNow.Add(CacheDuration));
                if (historyPixelId.HasValue)
                {
                    _historyCache.Remove(historyPixelId.Value);
                }
            }
        }
    }

    private static string ParseErrorMessage(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        return raw.Trim().Trim('"');
    }

    private async Task PersistAuthenticatedUserAsync(UserDto user, Guid sessionId)
    {
        await _localStorage.SetItemAsync(LocalStorageKey.UserName, user.UserName);
        await _localStorage.SetItemAsync(LocalStorageKey.Email, user.Email);
        await _localStorage.SetItemAsync(LocalStorageKey.LoginMethod, user.LoginMethod);
        if (user.Id.HasValue)
        {
            await _localStorage.SetItemAsync(LocalStorageKey.UserId, user.Id.Value);
        }
        else
        {
            await _localStorage.RemoveItemAsync(LocalStorageKey.UserId);
        }

        await SetSessionAsync(sessionId);
    }

    private static Exception CreateCanvasManagementException(HttpStatusCode statusCode, string errorMessage, string action)
    {
        if (statusCode == HttpStatusCode.ServiceUnavailable)
            return new Exception("Service is currently unavailable. Please try again later.");
        if (statusCode == HttpStatusCode.NotFound)
            return new Exception("Canvas is not found.");
        if (statusCode == HttpStatusCode.Forbidden)
            return new UnauthorizedAccessException($"Only the canvas creator can {action} this canvas.");
        if (statusCode == HttpStatusCode.Unauthorized)
            return new UnauthorizedAccessException("You are not authorized. Please log in again.");
        if (statusCode == HttpStatusCode.BadRequest)
            return new Exception(errorMessage);
        return new Exception(errorMessage);
    }

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
