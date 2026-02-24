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
    public async Task<IActionResult> GetByPixelDto(string canvasName, [FromBody]PixelDto pixelDto)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
            return Unauthorized("Session-Id header missing or invalid.");

        var userId = _sessionService.GetUserId(sessionId);
        if (userId == null)
            return Unauthorized("Invalid session.");

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
            return NotFound("Canvas not found.");
        var pixelDtoReq = new PixelDto
        {
            CanvasId = canvas.Id,
            X = pixelDto.X,
            Y = pixelDto.Y,
        };
        var pixelExtracted = await _repoManager.PixelRepository.GetByPixelDto(pixelDtoReq);
        if (pixelExtracted == null)
        {
            _logger.LogInformation("Pixel not found, returning default pixel.");
            pixelExtracted = await GetDefaultPixel(pixelDtoReq);
        }
        return Ok(pixelExtracted);
    }

    private async Task<PixelDto> GetDefaultPixel(PixelDto pixelDtoReq)
    {
        var defaultColor = await _repoManager.ColorRepository.GetDefautColor();
         // Should depend on canvas type.
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
            return Unauthorized("Session-Id header missing or invalid.");

        var userId = _sessionService.GetUserId(sessionId);
        if (userId == null)
            return Unauthorized("Invalid session.");
        
        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
            return NotFound("Canvas not found.");
        pixel.CanvasId = canvas.Id;
        var result = await _repoManager.PixelRepository.TryChangePixelAsync(userId.Value, pixel);
        if (result == null)
            return BadRequest("Could not change pixel.");
        _changedPixelsChannel.Writer.TryWrite(result);
        return Ok(result);
    }
}

