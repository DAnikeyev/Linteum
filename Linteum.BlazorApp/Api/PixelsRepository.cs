using System.Globalization;
using System.Net;
using Linteum.BlazorApp.ExtensionMethods;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>
/// Pixel resource access: get, single paint, batch paint (pixel + coordinate forms), batch delete,
/// text draw, and the Normal-mode quota. Extracted from <c>MyApiClient</c> (P‑MAIN‑03). Pixel/history
/// caches are read and written through <see cref="PixelCacheManager"/>.
/// </summary>
internal sealed class PixelsRepository
{
    private readonly ApiHttp _http;
    private readonly PixelCacheManager _cache;
    private readonly ILogger<PixelsRepository> _logger;

    public PixelsRepository(ApiHttp http, PixelCacheManager cache, ILogger<PixelsRepository> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<NormalModeQuotaDto?> GetNormalModeQuotaAsync(string canvasName)
    {
        var request = await _http.CreateAsync(HttpMethod.Get, $"/pixels/quota/{Uri.EscapeDataString(canvasName)}");
        var response = await _http.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NormalModeQuotaDto>();
    }

    public async Task<PixelDto> GetPixelData(string canvasName, int x, int y, bool useCache = false)
    {
        _logger.LogInformation("GetPixelData called with canvasName: {CanvasName}, x: {X}, y: {Y}, useCache: {UseCache}", canvasName, x, y, useCache);
        if (useCache)
        {
            if (_cache.TryGetPixel(canvasName, x, y, out var cachedPixel))
            {
                return cachedPixel;
            }
        }

        var pixelDto = new PixelDto
        {
            X = x,
            Y = y,
        };
        var request = await _http.CreateAsync(HttpMethod.Get, $"pixels/getpixel/{canvasName}");
        request.SetJsonContent(pixelDto);
        var response = await _http.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var pixel = await response.Content.ReadFromJsonAsync<PixelDto>();
        var result = pixel ?? new PixelDto();
        _cache.SetPixel(canvasName, x, y, result);
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
        var request = await _http.CreateAsync(HttpMethod.Post, $"/pixels/change/{canvasDto.Name}");
        request.SetJsonContent(pixelDto);
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var paintedPixel = await response.Content.ReadFromJsonAsync<PixelDto>();
            if (paintedPixel == null)
            {
                _logger.LogError("Painted pixel data is null for {CanvasName} at ({X}, {Y})", canvasDto.Name, clickedPixel.X, clickedPixel.Y);
                throw new Exception("Painted pixel data is null.");
            }

            _cache.StorePaintedPixel(canvasDto.Name, paintedPixel);

            _logger.LogInformation("Successfully painted pixel at ({X}, {Y}) on {CanvasName} with price {Price}", clickedPixel.X, clickedPixel.Y, canvasDto.Name, paintedPixel.Price);
            return paintedPixel;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to paint pixel at ({X}, {Y}) on {CanvasName}, Status: {StatusCode}, Error: {Error}", clickedPixel.X, clickedPixel.Y, canvasDto.Name, response.StatusCode, errorContent);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ApiErrors.ParseErrorMessage(errorContent, "Cannot paint pixel. Possibly insufficient funds."));
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("Canvas or color is not found.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
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
        var request = await _http.CreateAsync(HttpMethod.Post, requestUri);
        request.SetJsonContent(requestDto);
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<PixelBatchChangeResultDto>();
            if (result == null)
            {
                _logger.LogError("PaintBatch returned null result for {CanvasName}", canvasDto.Name);
                throw new Exception("Batch paint result is null.");
            }

            _cache.ApplyBatchPaintCache(canvasDto.Name, result.ChangedPixels);
            _logger.LogInformation("Successfully painted batch on {CanvasName}. Requested={RequestedCount}, Successful={SuccessfulCount}", canvasDto.Name, result.RequestedCount, result.ChangedPixels.Count);
            return result;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to paint batch on {CanvasName}, Status: {StatusCode}, Error: {Error}", canvasDto.Name, response.StatusCode, errorContent);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ApiErrors.ParseErrorMessage(errorContent, "Cannot paint pixels. Possibly insufficient funds or quota."));
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

        var request = await _http.CreateAsync(HttpMethod.Post, $"/pixels/delete-batch/{canvasDto.Name}");
        request.SetJsonContent(requestDto);
        var response = await _http.Client.SendAsync(request);

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
                _cache.HandlePixelDeleted(canvasDto.Name, coord.X, coord.Y, canvasDto.Id);
            }

            return result;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to delete batch on {CanvasName}, Status: {StatusCode}, Error: {Error}", canvasDto.Name, response.StatusCode, errorContent);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new Exception("Service is currently unavailable. Please try again later.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception(ApiErrors.ParseErrorMessage(errorContent, "Cannot delete pixels."));
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

        var request = await _http.CreateAsync(HttpMethod.Post, $"/pixels/text/{canvasDto.Name}");
        request.SetJsonContent(requestDto);
        var response = await _http.Client.SendAsync(request);

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
            throw new Exception(ApiErrors.ParseErrorMessage(errorContent, "Cannot queue text drawing."));
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new Exception(ApiErrors.ParseErrorMessage(errorContent, "Canvas or color is not found."));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("You are not authorized to draw text on this canvas.");
        throw new Exception($"Failed to queue text drawing at ({requestDto.X}, {requestDto.Y}). This exception is unexpected.");
    }
}
