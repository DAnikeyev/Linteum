using System.Net;
using Linteum.BlazorApp.ExtensionMethods;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>Per-canvas chat message access. Extracted from <c>MyApiClient</c> (P‑MAIN‑03).</summary>
internal sealed class CanvasChatRepository
{
    private readonly ApiHttp _http;
    private readonly ILogger<CanvasChatRepository> _logger;

    public CanvasChatRepository(ApiHttp http, ILogger<CanvasChatRepository> logger)
    {
        _http = http;
        _logger = logger;
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

        var request = await _http.CreateAsync(HttpMethod.Post, $"/canvaschat/{Uri.EscapeDataString(canvasName)}");
        request.SetJsonContent(new SendCanvasChatMessageRequestDto
        {
            Message = normalizedMessage,
        });

        var response = await _http.Client.SendAsync(request);
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
            throw new Exception(ApiErrors.ParseErrorMessage(errorContent, "Cannot send chat message."));
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("You are not authorized to send chat messages on this canvas.");

        throw new Exception(ApiErrors.ParseErrorMessage(errorContent, $"Failed to send a chat message to {canvasName}."));
    }
}
