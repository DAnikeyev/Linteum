using System.Threading.Channels;
using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PixelsController : ControllerBase
{
    private readonly RepositoryManager _repoManager;
    private readonly ILogger<PixelsController> _logger;
    private readonly Channel<PixelDto> _changedPixelsChannel;
    private readonly IPixelChangeCounter _pixelChangeCounter;
    private readonly ITextDrawQueue _textDrawQueue;
    private readonly SessionService _sessionService;
    private readonly IPixelNotifier _pixelNotifier;

    public PixelsController(RepositoryManager repoManager, SessionService sessionService, ILogger<PixelsController> logger, Channel<PixelDto> changedPixelsChannel, IPixelChangeCounter pixelChangeCounter, ITextDrawQueue textDrawQueue, IPixelNotifier pixelNotifier)
    {
        _sessionService = sessionService;
        _repoManager = repoManager;
        _logger = logger;
        _changedPixelsChannel = changedPixelsChannel;
        _pixelChangeCounter = pixelChangeCounter;
        _textDrawQueue = textDrawQueue;
        _pixelNotifier = pixelNotifier;
    }

    [HttpGet("canvases/{canvasId}")]
    public async Task<IActionResult> GetByCanvasId(Guid canvasId)
    {
        // WARNING: This can return millions of pixels and cause OOM.
        // For large canvases, use /canvases/image/{name} and SignalR for updates.
        var pixels = (await _repoManager.PixelRepository.GetByCanvasIdAsync(canvasId)).ToList();
        
        if (pixels.Count > 100000)
        {
            _logger.LogWarning("Returning a large number of pixels ({Count}) for canvas {CanvasId}. This may cause performance issues or OOM.", pixels.Count, canvasId);
        }

        _logger.LogInformation("Pixels for canvas {CanvasId} returned successfully. Count={Count}", canvasId, pixels.Count);
        return Ok(pixels);
    }
    
    
    [HttpGet("getpixel/{canvasName}")]
    public async Task<IActionResult> GetByPixelDto(string canvasName, [FromBody]PixelDto pixelDto)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
        {
            _logger.LogWarning("GetByPixelDto failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (userId == null)
        {
            _logger.LogWarning("GetByPixelDto failed: Invalid session for sessionId: {SessionId}", sessionId);
            return Unauthorized("Invalid session.");
        }

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("GetByPixelDto failed: Canvas {CanvasName} not found.", canvasName);
            return NotFound("Canvas not found.");
        }
        var pixelDtoReq = new PixelDto
        {
            CanvasId = canvas.Id,
            X = pixelDto.X,
            Y = pixelDto.Y,
        };
        var pixelExtracted = await _repoManager.PixelRepository.GetByPixelDto(pixelDtoReq);
        if (pixelExtracted == null)
        {
            pixelExtracted = await GetDefaultPixel(pixelDtoReq);
            _logger.LogInformation("Pixel lookup for canvas {CanvasName} at ({X}, {Y}) returned the default pixel for user {UserId}.", canvasName, pixelDto.X, pixelDto.Y, userId.Value);
            return Ok(pixelExtracted);
        }

        _logger.LogInformation("Pixel lookup for canvas {CanvasName} at ({X}, {Y}) succeeded for user {UserId}.", canvasName, pixelDto.X, pixelDto.Y, userId.Value);
        return Ok(pixelExtracted);
    }

    private async Task<PixelDto> GetDefaultPixel(PixelDto pixelDtoReq)
    {
        var defaultColor = await _repoManager.ColorRepository.GetDefautColor();
        if (defaultColor == null)
        {
            throw new InvalidOperationException("Default color is not configured.");
        }

        return new PixelDto
        {
            CanvasId = pixelDtoReq.CanvasId,
            X = pixelDtoReq.X,
            Y = pixelDtoReq.Y,
            OwnerId = null,
            Id = null,
            Price = 0,
            ColorId = defaultColor.Id,
        };
    }

    [HttpGet("owner/{ownerId}")]
    public async Task<IActionResult> GetByOwnerId(Guid ownerId)
    {
        var pixels = (await _repoManager.PixelRepository.GetByOwnerIdAsync(ownerId)).ToList();
        _logger.LogInformation("Pixels for owner {OwnerId} returned successfully. Count={Count}", ownerId, pixels.Count);
        return Ok(pixels);
    }

    [HttpGet("quota/{canvasName}")]
    public async Task<IActionResult> GetNormalModeQuota(string canvasName)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
        {
            _logger.LogWarning("GetNormalModeQuota failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (userId == null)
        {
            _logger.LogWarning("GetNormalModeQuota failed: Invalid session for sessionId: {SessionId}", sessionId);
            return Unauthorized("Invalid session.");
        }

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("GetNormalModeQuota failed: Canvas {CanvasName} not found.", canvasName);
            return NotFound("Canvas not found.");
        }

        var quota = await _repoManager.PixelRepository.GetNormalModeQuotaAsync(userId.Value, canvas.Id);
        return Ok(quota);
    }

    [HttpPost("change/{canvasName}")]
    public async Task<IActionResult> TryChangePixel(string canvasName, [FromBody] PixelDto pixel)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
        {
            _logger.LogWarning("TryChangePixel failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (userId == null)
        {
            _logger.LogWarning("TryChangePixel failed: Invalid session for sessionId: {SessionId}", sessionId);
            return Unauthorized("Invalid session.");
        }
        
        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("TryChangePixel failed: Canvas {CanvasName} not found.", canvasName);
            return NotFound("Canvas not found.");
        }
        pixel.CanvasId = canvas.Id;
        if (ShouldLogZeroPriceFreeDrawAtDebug(canvas.CanvasMode, false, [pixel]))
        {
            _logger.LogDebug("TryChangePixel requested for canvas {CanvasName} in {CanvasMode} mode by user {UserId} at ({X}, {Y}) with color {ColorId} and price {Price}.", canvasName, canvas.CanvasMode, userId.Value, pixel.X, pixel.Y, pixel.ColorId, pixel.Price);
        }
        else
        {
            _logger.LogInformation("TryChangePixel requested for canvas {CanvasName} in {CanvasMode} mode by user {UserId} at ({X}, {Y}) with color {ColorId} and price {Price}.", canvasName, canvas.CanvasMode, userId.Value, pixel.X, pixel.Y, pixel.ColorId, pixel.Price);
        }

        var result = await _repoManager.PixelRepository.TryChangePixelAsync(userId.Value, pixel);
        if (result == null)
        {
            _logger.LogWarning("TryChangePixel failed for user {UserId} at ({X}, {Y}) on {CanvasName}. Mode={CanvasMode}, Price={Price}", userId.Value, pixel.X, pixel.Y, canvasName, canvas.CanvasMode, pixel.Price);
            var message = canvas.CanvasMode == CanvasMode.Economy
                ? "Could not change pixel. Check bid amount and gold balance."
                : canvas.CanvasMode == CanvasMode.Normal
                    ? "Could not change pixel. Normal mode allows up to 100 successful pixel changes per day on each canvas."
                    : "Could not change pixel.";
            return BadRequest(message);
        }
        _changedPixelsChannel.Writer.TryWrite(result);
        _pixelChangeCounter.RecordSuccess(canvasName);
        if (ShouldLogZeroPriceFreeDrawAtDebug(canvas.CanvasMode, false, [result]))
        {
            _logger.LogDebug("TryChangePixel succeeded for user {UserId} at ({X}, {Y}) on {CanvasName}. PixelId={PixelId}, Price={Price}", userId.Value, result.X, result.Y, canvasName, result.Id, result.Price);
        }
        else
        {
            _logger.LogInformation("TryChangePixel succeeded for user {UserId} at ({X}, {Y}) on {CanvasName}. PixelId={PixelId}, Price={Price}", userId.Value, result.X, result.Y, canvasName, result.Id, result.Price);
        }

        return Ok(result);
    }

    [HttpPost("change-batch/{canvasName}")]
    public async Task<IActionResult> TryChangePixelsBatch(string canvasName, [FromBody] PixelBatchChangeRequestDto request)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
        {
            _logger.LogWarning("TryChangePixelsBatch failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (userId == null)
        {
            _logger.LogWarning("TryChangePixelsBatch failed: Invalid session for sessionId: {SessionId}", sessionId);
            return Unauthorized("Invalid session.");
        }

        if (request.Pixels.Count == 0)
        {
            _logger.LogWarning("TryChangePixelsBatch failed for user {UserId}: no pixels were provided.", userId.Value);
            return BadRequest("At least one pixel is required.");
        }

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("TryChangePixelsBatch failed: Canvas {CanvasName} not found.", canvasName);
            return NotFound("Canvas not found.");
        }

        foreach (var pixel in request.Pixels)
        {
            pixel.CanvasId = canvas.Id;
        }

        var useMasterOverride = IsValidMasterPassword(request.MasterPassword);
        return await ExecuteBatchChangeAsync(canvasName, canvas.CanvasMode, userId.Value, request.Pixels, useMasterOverride);
    }

    [HttpPost("change-batch-coordinates/{canvasName}")]
    public async Task<IActionResult> TryChangePixelsBatch(string canvasName, [FromBody] PixelBatchDto request)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
        {
            _logger.LogWarning("TryChangePixelsBatch failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (userId == null)
        {
            _logger.LogWarning("TryChangePixelsBatch failed: Invalid session for sessionId: {SessionId}", sessionId);
            return Unauthorized("Invalid session.");
        }

        if (request.Coordinates.Count == 0)
        {
            _logger.LogWarning("TryChangePixelsBatch failed for user {UserId}: no coordinates were provided.", userId.Value);
            return BadRequest("At least one coordinate is required.");
        }

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("TryChangePixelsBatch failed: Canvas {CanvasName} not found.", canvasName);
            return NotFound("Canvas not found.");
        }

        var pixels = request.Coordinates
            .Select(coordinate => new PixelDto
            {
                X = coordinate.X,
                Y = coordinate.Y,
                ColorId = request.ColorId,
                Price = request.Price,
                CanvasId = canvas.Id,
            })
            .ToList();

        var useMasterOverride = IsValidMasterPassword(request.MasterPassword);
        return await ExecuteBatchChangeAsync(canvasName, canvas.CanvasMode, userId.Value, pixels, useMasterOverride, request.Playback, request.Coordinates);
    }

    [HttpPost("delete-batch/{canvasName}")]
    public async Task<IActionResult> TryDeletePixelsBatch(string canvasName, [FromBody] PixelBatchDeleteRequestDto request)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
        {
            _logger.LogWarning("TryDeletePixelsBatch failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (userId == null)
        {
            _logger.LogWarning("TryDeletePixelsBatch failed: Invalid session for sessionId: {SessionId}", sessionId);
            return Unauthorized("Invalid session.");
        }

        if (request.Coordinates.Count == 0)
        {
            _logger.LogWarning("TryDeletePixelsBatch failed for user {UserId}: no coordinates were provided.", userId.Value);
            return BadRequest("At least one coordinate is required.");
        }

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("TryDeletePixelsBatch failed: Canvas {CanvasName} not found.", canvasName);
            return NotFound("Canvas not found.");
        }

        var useMasterOverride = IsValidMasterPassword(request.MasterPassword);
        _logger.LogInformation(
            "TryDeletePixelsBatch requested for canvas {CanvasName} by user {UserId}. RequestedCount={RequestedCount}, UsedMasterOverride={UsedMasterOverride}",
            canvasName,
            userId.Value,
            request.Coordinates.Count,
            useMasterOverride);

        var playback = CreatePlaybackMetadataOrNull(canvas.CanvasMode, request.Playback);
        var deleteResult = await _repoManager.PixelRepository.TryDeletePixelsBatchAsync(userId.Value, request.Coordinates, canvas.Id, useMasterOverride, suppressNotifications: playback != null);
        var orderedDeletedCoordinates = ReorderCoordinates(deleteResult.DeletedCoordinates, request.Coordinates);

        if (playback != null && orderedDeletedCoordinates.Count > 0)
        {
            await _pixelNotifier.NotifyConfirmedPixelsDeleted(canvasName, new ConfirmedPixelDeletionPlaybackBatchDto
            {
                ClientOperationId = playback.ClientOperationId,
                StrokeId = playback.StrokeId,
                ChunkSequence = playback.ChunkSequence ?? 0,
                DurationMs = NormalizePlaybackDuration(playback),
                Coordinates = orderedDeletedCoordinates,
            });
        }

        _logger.LogInformation(
            "TryDeletePixelsBatch completed for user {UserId} on {CanvasName}. DeletedCount={DeletedCount}",
            userId.Value,
            canvasName,
            deleteResult.DeletedCount);

        deleteResult.DeletedCoordinates = orderedDeletedCoordinates;
        return Ok(deleteResult);
    }

    private async Task<IActionResult> ExecuteBatchChangeAsync(string canvasName, CanvasMode canvasMode, Guid userId, IReadOnlyCollection<PixelDto> pixels, bool useMasterOverride, StrokePlaybackMetadataDto? playbackRequest = null, IReadOnlyCollection<CoordinateDto>? requestedCoordinates = null)
    {
        if (ShouldLogZeroPriceFreeDrawAtDebug(canvasMode, useMasterOverride, pixels))
        {
            _logger.LogDebug(
                "TryChangePixelsBatch requested for canvas {CanvasName} in {CanvasMode} mode by user {UserId}. RequestedPixels={RequestedCount}, UsedMasterOverride={UsedMasterOverride}",
                canvasName,
                canvasMode,
                userId,
                pixels.Count,
                useMasterOverride);
        }
        else
        {
            _logger.LogInformation(
                "TryChangePixelsBatch requested for canvas {CanvasName} in {CanvasMode} mode by user {UserId}. RequestedPixels={RequestedCount}, UsedMasterOverride={UsedMasterOverride}",
                canvasName,
                canvasMode,
                userId,
                pixels.Count,
                useMasterOverride);
        }

        var playback = CreatePlaybackMetadataOrNull(canvasMode, playbackRequest);
        var result = await _repoManager.PixelRepository.TryChangePixelsBatchAsync(userId, pixels.ToList(), useMasterOverride, suppressNotifications: playback != null);
        var orderedChangedPixels = ReorderPixels(result.ChangedPixels, requestedCoordinates ?? pixels.Select(pixel => new CoordinateDto(pixel.X, pixel.Y)).ToList());
        result.ChangedPixels = orderedChangedPixels;

        foreach (var changedPixel in result.ChangedPixels)
        {
            _changedPixelsChannel.Writer.TryWrite(changedPixel);
            _pixelChangeCounter.RecordSuccess(canvasName);
        }

        if (playback != null && result.ChangedPixels.Count > 0)
        {
            await _pixelNotifier.NotifyConfirmedPixelsChanged(canvasName, new ConfirmedPixelPlaybackBatchDto
            {
                ClientOperationId = playback.ClientOperationId,
                StrokeId = playback.StrokeId,
                ChunkSequence = playback.ChunkSequence ?? 0,
                DurationMs = NormalizePlaybackDuration(playback),
                Pixels = result.ChangedPixels,
            });
        }

        if (ShouldLogZeroPriceFreeDrawAtDebug(canvasMode, useMasterOverride, pixels))
        {
            _logger.LogDebug(
                "TryChangePixelsBatch completed for user {UserId} on {CanvasName}. Requested={RequestedCount}, Deduplicated={DeduplicatedCount}, Successful={SuccessfulCount}, StoppedByBudget={StoppedByBudget}, StoppedByNormalModeLimit={StoppedByNormalModeLimit}, UsedMasterOverride={UsedMasterOverride}",
                userId,
                canvasName,
                result.RequestedCount,
                result.DeduplicatedCount,
                result.ChangedPixels.Count,
                result.StoppedByBudget,
                result.StoppedByNormalModeLimit,
                result.UsedMasterOverride);
        }
        else
        {
            _logger.LogInformation(
                "TryChangePixelsBatch completed for user {UserId} on {CanvasName}. Requested={RequestedCount}, Deduplicated={DeduplicatedCount}, Successful={SuccessfulCount}, StoppedByBudget={StoppedByBudget}, StoppedByNormalModeLimit={StoppedByNormalModeLimit}, UsedMasterOverride={UsedMasterOverride}",
                userId,
                canvasName,
                result.RequestedCount,
                result.DeduplicatedCount,
                result.ChangedPixels.Count,
                result.StoppedByBudget,
                result.StoppedByNormalModeLimit,
                result.UsedMasterOverride);
        }

        if (result.ChangedPixels.Count == 0)
        {
            if (result.StoppedByBudget)
            {
                return BadRequest("Could not change pixels. Check bid amounts and gold balance.");
            }

            if (result.StoppedByNormalModeLimit)
            {
                return BadRequest("Could not change pixels. Normal mode allows up to 100 successful pixel changes per day on each canvas.");
            }

            return BadRequest("Could not change any pixels.");
        }

        return Ok(result);
    }

    private static StrokePlaybackMetadataDto? CreatePlaybackMetadataOrNull(CanvasMode canvasMode, StrokePlaybackMetadataDto? playback)
    {
        if (canvasMode != CanvasMode.FreeDraw || playback == null || !playback.IsValid())
        {
            return null;
        }

        return playback;
    }

    private static bool ShouldLogZeroPriceFreeDrawAtDebug(CanvasMode canvasMode, bool useMasterOverride, IReadOnlyCollection<PixelDto> pixels)
    {
        return !useMasterOverride
            && canvasMode == CanvasMode.FreeDraw
            && pixels.Count > 0
            && pixels.All(pixel => pixel.Price <= 0);
    }

    private static int NormalizePlaybackDuration(StrokePlaybackMetadataDto playback)
    {
        if (!playback.ChunkDurationMs.HasValue)
        {
            return 0;
        }

        return Math.Max(0, playback.ChunkDurationMs.Value);
    }

    private static List<PixelDto> ReorderPixels(IReadOnlyCollection<PixelDto> changedPixels, IReadOnlyCollection<CoordinateDto> requestOrder)
    {
        if (changedPixels.Count <= 1)
        {
            return changedPixels.ToList();
        }

        var orderByCoordinate = requestOrder
            .Select((coordinate, index) => new { coordinate.X, coordinate.Y, Index = index })
            .GroupBy(item => (item.X, item.Y))
            .ToDictionary(group => group.Key, group => group.Last().Index);

        return changedPixels
            .OrderBy(pixel => orderByCoordinate.TryGetValue((pixel.X, pixel.Y), out var index) ? index : int.MaxValue)
            .ToList();
    }

    private static List<CoordinateDto> ReorderCoordinates(IReadOnlyCollection<CoordinateDto> changedCoordinates, IReadOnlyCollection<CoordinateDto> requestOrder)
    {
        if (changedCoordinates.Count <= 1)
        {
            return changedCoordinates.ToList();
        }

        var orderByCoordinate = requestOrder
            .Select((coordinate, index) => new { coordinate.X, coordinate.Y, Index = index })
            .GroupBy(item => (item.X, item.Y))
            .ToDictionary(group => group.Key, group => group.Last().Index);

        return changedCoordinates
            .OrderBy(coordinate => orderByCoordinate.TryGetValue((coordinate.X, coordinate.Y), out var index) ? index : int.MaxValue)
            .ToList();
    }

    [HttpPost("text/{canvasName}")]
    public async Task<IActionResult> QueueTextDraw(string canvasName, [FromBody] TextDrawRequestDto request)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
        {
            _logger.LogWarning("QueueTextDraw failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (userId == null)
        {
            _logger.LogWarning("QueueTextDraw failed: Invalid session for sessionId: {SessionId}", sessionId);
            return Unauthorized("Invalid session.");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            _logger.LogWarning("QueueTextDraw failed for user {UserId}: text was empty for canvas {CanvasName}.", userId.Value, canvasName);
            return BadRequest("Text is required.");
        }

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("QueueTextDraw failed: Canvas {CanvasName} not found.", canvasName);
            return NotFound("Canvas not found.");
        }

        if (canvas.CanvasMode != CanvasMode.FreeDraw)
        {
            _logger.LogWarning("QueueTextDraw failed: Canvas {CanvasName} is not free draw. Mode={CanvasMode}", canvasName, canvas.CanvasMode);
            return BadRequest("Text drawing is only available for FreeDraw canvases.");
        }

        var colorsById = (await _repoManager.ColorRepository.GetAllAsync())
            .ToDictionary(color => color.Id);

        if (!colorsById.TryGetValue(request.TextColorId, out var textColor))
        {
            _logger.LogWarning("QueueTextDraw failed: text color {ColorId} not found for user {UserId} on canvas {CanvasName}.", request.TextColorId, userId.Value, canvasName);
            return NotFound("Text color not found.");
        }

        ColorDto? backgroundColor = null;
        if (request.BackgroundColorId is { } backgroundColorId && !colorsById.TryGetValue(backgroundColorId, out backgroundColor))
        {
            _logger.LogWarning("QueueTextDraw failed: background color {ColorId} not found for user {UserId} on canvas {CanvasName}.", backgroundColorId, userId.Value, canvasName);
            return NotFound("Background color not found.");
        }

        await _textDrawQueue.QueueAsync(new QueuedTextDrawRequest(
            userId.Value,
            canvas.Name,
            canvas.Id,
            request.X,
            request.Y,
            request.Text,
            request.FontSize,
            textColor,
            backgroundColor));

        _logger.LogInformation(
            "Queued text draw for user {UserId} on {CanvasName} at ({X}, {Y}) with text length {TextLength}.",
            userId.Value,
            canvasName,
            request.X,
            request.Y,
            request.Text.Length);

        return Accepted();
    }

    private static bool IsValidMasterPassword(string? providedMasterPassword)
    {
        if (string.IsNullOrWhiteSpace(providedMasterPassword))
        {
            return false;
        }

        var configuredMasterPassword = Environment.GetEnvironmentVariable("MASTER_PASSWORD");
        return !string.IsNullOrWhiteSpace(configuredMasterPassword)
               && string.Equals(configuredMasterPassword, providedMasterPassword, StringComparison.Ordinal);
    }
}

