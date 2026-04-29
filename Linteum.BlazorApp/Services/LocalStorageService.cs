using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Linteum.BlazorApp.Services;

namespace Linteum.BlazorApp;

public class LocalStorageService
{
    protected ProtectedLocalStorage LocalStorage { get; set; }
    protected ProtectedSessionStorage SessionStorage { get; set; }
    
    public LocalStorageService(ProtectedLocalStorage localStorage, ProtectedSessionStorage sessionStorage)
    {
        LocalStorage = localStorage;
        SessionStorage = sessionStorage;
    }
    
    public async Task SetItemAsync<T>(LocalStorageKey key, T? value)
    {
        await LocalStorage.SetAsync(key.ToString(), value!);
    }
    
    public async Task<T?> GetItemAsync<T>(LocalStorageKey key)
    {
        var result = await LocalStorage.GetAsync<T>(key.ToString());
        return result.Success ? result.Value : default;
    }

    public async Task SetSessionItemAsync<T>(SessionStorageKey key, T? value)
    {
        await SessionStorage.SetAsync(key.ToString(), value!);
    }

    public async Task<T?> GetSessionItemAsync<T>(SessionStorageKey key)
    {
        var result = await SessionStorage.GetAsync<T>(key.ToString());
        return result.Success ? result.Value : default;
    }

    public async Task RemoveSessionItemAsync(SessionStorageKey key)
    {
        await SessionStorage.DeleteAsync(key.ToString());
    }
    
    public async Task RemoveItemAsync(LocalStorageKey key)
    {
        await LocalStorage.DeleteAsync(key.ToString());
    }

    public async Task ClearAsync()
    {
        foreach (LocalStorageKey key in Enum.GetValues(typeof(LocalStorageKey)))
        {
            await LocalStorage.DeleteAsync(key.ToString());
        }

        foreach (SessionStorageKey key in Enum.GetValues(typeof(SessionStorageKey)))
        {
            await SessionStorage.DeleteAsync(key.ToString());
        }
    }
}