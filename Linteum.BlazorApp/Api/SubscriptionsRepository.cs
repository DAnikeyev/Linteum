using System.Net;
using Linteum.BlazorApp.ExtensionMethods;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>Canvas subscribe/unsubscribe access. Extracted from <c>MyApiClient</c> (P‑MAIN‑03).</summary>
internal sealed class SubscriptionsRepository
{
    private readonly ApiHttp _http;
    private readonly ILogger<SubscriptionsRepository> _logger;

    public SubscriptionsRepository(ApiHttp http, ILogger<SubscriptionsRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> SubscribeAsync(string canvasName, string? password)
    {
        _logger.LogInformation("SubscribeAsync called with canvasName: {CanvasName}", canvasName);
        var requestDto = new SubscribeCanvasRequestDto
        {
            Canvas = new CanvasDto { Name = canvasName },
            Password = string.IsNullOrEmpty(password) ? null : password,
        };

        var request = await _http.CreateAsync(HttpMethod.Post, "/canvases/subscribe");
        request.SetJsonContent(requestDto);
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully subscribed to canvas: {CanvasName}", canvasName);
            return true;
        }

        _logger.LogWarning("Failed to subscribe to canvas: {CanvasName}, Status: {StatusCode}", canvasName, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception("Cannot subscribe to canvas");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to subscribe to {canvasName}. This exception is unexpected.");
    }

    public async Task<bool> UnsubscribeAsync(string canvasName)
    {
        _logger.LogInformation("UnsubscribeAsync called with canvasName: {CanvasName}", canvasName);
        var canvasDto = new CanvasDto { Name = canvasName };
        var request = await _http.CreateAsync(HttpMethod.Post, "/canvases/unsubscribe");
        request.SetJsonContent(canvasDto);
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully unsubscribed from canvas: {CanvasName}", canvasName);
            return true;
        }

        _logger.LogWarning("Failed to unsubscribe from canvas {CanvasName}, Status: {StatusCode}", canvasName, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception($"Cannot unsubscribe from canvas {canvasName}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to unsubscribe from {canvasName}. This exception is unexpected.");
    }
}
