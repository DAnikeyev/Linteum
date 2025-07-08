using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Linteum.BlazorApp;

public class LocalStorageService
{
    protected ProtectedLocalStorage LocalStorage { get; set; }
    
    public LocalStorageService(ProtectedLocalStorage localStorage)
    {
        LocalStorage = localStorage;
    }
    
    public async Task SetItemAsync<T>(LocalStorageKey key, T value)
    {
        await LocalStorage.SetAsync(key.ToString(), value);
    }
    
    public async Task<T?> GetItemAsync<T>(LocalStorageKey key)
    {
        var result = await LocalStorage.GetAsync<T>(key.ToString());
        return result.Success ? result.Value : default;
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
    }
}