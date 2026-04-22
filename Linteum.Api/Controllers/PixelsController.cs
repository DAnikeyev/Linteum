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
    private readonly SessionService _sessionService;

    public PixelsController(RepositoryManager repoManager, SessionService sessionService, ILogger<PixelsController> logger, Channel<PixelDto> changedPixelsChannel, IPixelChangeCounter pixelChangeCounter)
    {
        _sessionService = sessionService;
        _repoManager = repoManager;
        _logger = logger;
        _changedPixelsChannel = changedPixelsChannel;
        _pixelChangeCounter = pixelChangeCounter;
    }

    [HttpGet("canvases/{canvasId}")]
    public async Task<IActionResult> GetByCanvasId(Guid canvasId)
    {
        var pixels = (await _repoManager.PixelRepository.GetByCanvasIdAsync(canvasId)).ToList();
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
        var result = await _repoManager.PixelRepository.TryChangePixelAsync(userId.Value, pixel);
        if (result == null)
        {
            _logger.LogWarning("TryChangePixel failed for user {UserId} at ({X}, {Y}) on {CanvasName}.", userId, pixel.X, pixel.Y, canvasName);
            return BadRequest("Could not change pixel.");
        }
        _changedPixelsChannel.Writer.TryWrite(result);
        _pixelChangeCounter.RecordSuccess(canvasName);
        return Ok(result);
    }
}

