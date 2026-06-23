using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>
/// Session + local-storage persistence extracted from the <c>MyApiClient</c> god-class (P‑MAIN‑03).
/// Owns the <c>Session-Id</c>/<c>UserId</c>/<c>LoginMethod</c> reads and writes against
/// <see cref="LocalStorageService"/>, and clears the client caches (via <see cref="PixelCacheManager"/>)
/// on login/logout so no stale state from a previous user survives.
/// </summary>
internal sealed class SessionStore
{
    private readonly HttpClient _httpClient;
    private readonly LocalStorageService _localStorage;
    private readonly PixelCacheManager _cache;

    public SessionStore(HttpClient httpClient, LocalStorageService localStorage, PixelCacheManager cache)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
        _cache = cache;
    }

    public async Task SetSessionAsync(Guid? sessionId)
    {
        _cache.ClearAllCaches();
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
        _cache.ClearAllCaches();
        _httpClient.DefaultRequestHeaders.Remove(CustomHeaders.SessionId);
    }

    public async Task<Guid?> GetCurrentUserIdAsync()
    {
        return await _localStorage.GetItemAsync<Guid?>(LocalStorageKey.UserId);
    }

    public async Task<LoginMethod> GetCurrentLoginMethodAsync()
    {
        var loginMethod = await _localStorage.GetItemAsync<LoginMethod>(LocalStorageKey.LoginMethod);
        return loginMethod == 0 ? LoginMethod.Password : loginMethod;
    }

    public async Task<bool> IsGuestUserAsync()
    {
        return GuestUserHelper.IsGuest(await GetCurrentLoginMethodAsync());
    }

    /// <summary>Called by <see cref="AccountRepository"/> after a successful login/signup/validation.</summary>
    public async Task PersistAuthenticatedUserAsync(UserDto user, Guid sessionId)
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
}
