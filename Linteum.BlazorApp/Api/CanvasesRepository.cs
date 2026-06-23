using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Linteum.BlazorApp.ExtensionMethods;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>
/// Canvas resource access: create (blank + image), lookup, search, subscribed list, erase, delete,
/// image export. Extracted from <c>MyApiClient</c> (P‑MAIN‑03). Erase/delete clear the canvas cache
/// via <see cref="PixelCacheManager"/>.
/// </summary>
internal sealed class CanvasesRepository
{
    private readonly ApiHttp _http;
    private readonly PixelCacheManager _cache;
    private readonly ILogger<CanvasesRepository> _logger;

    public CanvasesRepository(ApiHttp http, PixelCacheManager cache, ILogger<CanvasesRepository> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CanvasDto?> AddCanvasAsync(CanvasDto canvasDto, string? password)
    {
        _logger.LogInformation("AddCanvasAsync called with canvas name: {CanvasName}", canvasDto.Name);
        var request = await _http.CreateAsync(HttpMethod.Post, "/canvases/Add");
        request.SetJsonContent(new CreateCanvasRequestDto { Canvas = canvasDto, Password = string.IsNullOrEmpty(password) ? null : password });

        var response = await _http.Client.SendAsync(request);
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

        using var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(new StringContent(canvasDto.Name), nameof(canvasDto.Name));
        multipartContent.Add(new StringContent(((int)canvasDto.CanvasMode).ToString(CultureInfo.InvariantCulture)), nameof(canvasDto.CanvasMode));

        if (!string.IsNullOrEmpty(password))
        {
            multipartContent.Add(new StringContent(password), "Password");
        }

        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipartContent.Add(imageContent, "Image", fileName);

        var request = await _http.CreateAsync(HttpMethod.Post, "/canvases/add-with-image");
        request.Content = multipartContent;

        var response = await _http.Client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync() ?? "No additional error information.";
            _logger.LogError("Failed to add canvas from image {CanvasName}. Status: {StatusCode}, Error: {Error}", canvasDto.Name, response.StatusCode, errorContent);
            throw new Exception($"Failed to add canvas from image. {ApiErrors.ParseErrorMessage(errorContent, "No additional error information.")}");
        }

        _logger.LogInformation("Canvas {CanvasName} added from image successfully", canvasDto.Name);
        return await response.Content.ReadFromJsonAsync<CanvasDto>();
    }

    public async Task<CanvasDto> GetCanvas(string canvasName)
    {
        _logger.LogInformation("GetCanvas called with canvasName: {CanvasName}", canvasName);

        var request = await _http.CreateAsync(HttpMethod.Get, $"/canvases/name/{canvasName}");
        var response = await _http.Client.SendAsync(request);

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
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception($"Canvas {canvasName} is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to get info of {canvasName}. This exception is unexpected.");
    }

    public async Task<List<CanvasDto>> SearchCanvasesAsync(string name)
    {
        _logger.LogInformation("SearchCanvasesAsync called with name: {Name}", name);
        if (string.IsNullOrWhiteSpace(name))
            return new List<CanvasDto>();

        var request = await _http.CreateAsync(HttpMethod.Get, $"/canvases/search?name={Uri.EscapeDataString(name)}");
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
            return canvases ?? new List<CanvasDto>();
        }

        _logger.LogWarning("Failed to search canvases with name: {Name}, Status: {StatusCode}", name, response.StatusCode);
        return new List<CanvasDto>();
    }

    public async Task<List<CanvasDto>> GetSubscribedCanvasesAsync()
    {
        _logger.LogInformation("GetSubscribedCanvasesAsync called");
        var request = await _http.CreateAsync(HttpMethod.Get, "canvases/subscribed");
        var response = await _http.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var canvases = await response.Content.ReadFromJsonAsync<List<CanvasDto>>();
        return canvases ?? new List<CanvasDto>();
    }

    public async Task<CanvasOperationResponseDto> EraseCanvasAsync(string canvasName)
    {
        _logger.LogInformation("EraseCanvasAsync called with canvasName: {CanvasName}", canvasName);
        var request = await _http.CreateAsync(HttpMethod.Post, $"/canvases/erase/{Uri.EscapeDataString(canvasName)}");
        var response = await _http.Client.SendAsync(request);

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
                _cache.ClearCanvasCache(canvasName);
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
        throw CreateCanvasManagementException(response.StatusCode, ApiErrors.ParseErrorMessage(errorContent, $"Failed to erase canvas {canvasName}."), "erase");
    }

    public async Task<CanvasOperationResponseDto> DeleteCanvasAsync(string canvasName)
    {
        _logger.LogInformation("DeleteCanvasAsync called with canvasName: {CanvasName}", canvasName);
        var request = await _http.CreateAsync(HttpMethod.Delete, $"/canvases/delete/{Uri.EscapeDataString(canvasName)}");
        var response = await _http.Client.SendAsync(request);

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
                _cache.ClearCanvasCache(canvasName);
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
        throw CreateCanvasManagementException(response.StatusCode, ApiErrors.ParseErrorMessage(errorContent, $"Failed to delete canvas {canvasName}."), "delete");
    }

    public async Task<byte[]> GetCanvasImage(CanvasDto canvasDto)
    {
        _logger.LogInformation("GetCanvasImage called with canvasName: {CanvasName}", canvasDto.Name);
        var request = await _http.CreateAsync(HttpMethod.Get, $"/canvases/image/{canvasDto.Name}");
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsByteArrayAsync();
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception($"Image for canvas {canvasDto.Name} is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("Password is incorrect.");
        throw new Exception($"Failed to get image of {canvasDto.Name}. This exception is unexpected.");
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
}
