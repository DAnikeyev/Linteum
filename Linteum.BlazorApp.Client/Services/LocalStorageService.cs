using Microsoft.JSInterop;
using System.Text.Json;

namespace Linteum.BlazorApp.Client;

/// <summary>
/// JS-interop localStorage wrapper that works in Interactive Server, WASM, and SSR contexts.
/// During static prerendering JS interop is unavailable; all methods gracefully return
/// defaults / no-op so callers don't need to guard against it.
/// </summary>
public class LocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js) => _js = js;

    public async Task SetItemAsync<T>(LocalStorageKey key, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            await _js.InvokeVoidAsync("localStorage.setItem", key.ToString(), json);
        }
        catch (InvalidOperationException) { /* SSR – JS interop unavailable */ }
    }

    public async Task<T?> GetItemAsync<T>(LocalStorageKey key)
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", key.ToString());
            if (json is null) return default;
            try { return JsonSerializer.Deserialize<T>(json); }
            catch { return default; }
        }
        catch (InvalidOperationException) { return default; /* SSR */ }
    }

    public async Task RemoveItemAsync(LocalStorageKey key)
    {
        try { await _js.InvokeVoidAsync("localStorage.removeItem", key.ToString()); }
        catch (InvalidOperationException) { /* SSR */ }
    }

    public async Task ClearAsync()
    {
        try
        {
            foreach (LocalStorageKey key in Enum.GetValues(typeof(LocalStorageKey)))
                await _js.InvokeVoidAsync("localStorage.removeItem", key.ToString());
        }
        catch (InvalidOperationException) { /* SSR */ }
    }
}

