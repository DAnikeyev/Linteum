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
    private readonly SessionService _sessionService;

    public PixelsController(RepositoryManager repoManager, SessionService sessionService, ILogger<PixelsController> logger, Channel<PixelDto> changedPixelsChannel)
    {
        _sessionService = sessionService;
        _repoManager = repoManager;
        _logger = logger;
        _changedPixelsChannel = changedPixelsChannel;
    }

    [HttpGet("canvases/{canvasId}")]
    public async Task<IActionResult> GetByCanvasId(Guid canvasId)
    {
        var pixels = await _repoManager.PixelRepository.GetByCanvasIdAsync(canvasId);
        return Ok(pixels);
    }
    
    
    [HttpGet("getpixel/{canvasName}")]
    public async Task<IActionResult> GetByPixelDto(string canvasName, [FromQuery] int x, [FromQuery] int y)
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
            X = x,
            Y = y,
        };
        var pixelExtracted = await _repoManager.PixelRepository.GetByPixelDto(pixelDtoReq);
        if (pixelExtracted == null)
        {
            _logger.LogInformation("Pixel not found at ({X}, {Y}) for canvas {CanvasName}, returning default pixel.", x, y, canvasName);
            pixelExtracted = await GetDefaultPixel(pixelDtoReq);
        }
        return Ok(pixelExtracted);
    }

    private async Task<PixelDto> GetDefaultPixel(PixelDto pixelDtoReq)
    {
        var defaultColor = await _repoManager.ColorRepository.GetDefautColor();
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
        var pixels = await _repoManager.PixelRepository.GetByOwnerIdAsync(ownerId);
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
        _logger.LogInformation("Successfully changed pixel at ({X}, {Y}) on {CanvasName} by user {UserId}.", pixel.X, pixel.Y, canvasName, userId);
        return Ok(result);
    }
}

