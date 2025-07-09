using System.Text;
using Linteum.Shared;

namespace Linteum.BlazorApp.ExtensionMethods;

public static class HttpRequest
{
    public static async Task AddSessionId(this HttpRequestMessage message, LocalStorageService localStorage)
    {
        if (localStorage == null)
            throw new ArgumentNullException(nameof(localStorage));

        var sessionId = await localStorage.GetItemAsync<string>(LocalStorageKey.SessionId);
        if (!string.IsNullOrEmpty(sessionId))
        {
            message.Headers.Add(CustomHeaders.SessionId, sessionId);
        }
    }

    public static void SetJsonContent<T>(this HttpRequestMessage request, T content)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(content);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
    }
}