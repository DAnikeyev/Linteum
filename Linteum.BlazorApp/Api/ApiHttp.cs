using Linteum.BlazorApp.ExtensionMethods;

namespace Linteum.BlazorApp.Api;

/// <summary>
/// Shared HTTP surface for the API gateway. Owns the server-side <see cref="HttpClient"/> and the
/// local-storage-backed session, and builds requests with the <c>Session-Id</c> header already
/// attached. Extracted from the <c>MyApiClient</c> god-class (P‑MAIN‑03) so each resource repository
/// can share one HTTP client instead of reaching into <c>MyApiClient</c>.
/// </summary>
internal sealed class ApiHttp
{
    public HttpClient Client { get; }
    public LocalStorageService Storage { get; }

    public ApiHttp(HttpClient httpClient, LocalStorageService localStorage)
    {
        Client = httpClient;
        Storage = localStorage;
    }

    /// <summary>
    /// Creates a request for <paramref name="requestUri"/> with the current <c>Session-Id</c> header
    /// attached (read from protected local storage). Mirrors the per-request pattern every endpoint
    /// in <c>MyApiClient</c> used via <see cref="HttpRequest.AddSessionId"/>.
    /// </summary>
    public async Task<HttpRequestMessage> CreateAsync(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        await request.AddSessionId(Storage);
        return request;
    }
}
