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
    private readonly SessionService _sessionService;

    public PixelsController(RepositoryManager repoManager, SessionService sessionService, ILogger<PixelsController> logger)
    {
        _sessionService = sessionService;
        _repoManager = repoManager;
        _logger = logger;
    }

    [HttpGet("canvases/{canvasId}")]
    public async Task<IActionResult> GetByCanvasId(Guid canvasId)
    {
        var pixels = await _repoManager.PixelRepository.GetByCanvasIdAsync(canvasId);
        return Ok(pixels);
    }
    
    
    [HttpGet("canvases/{canvasName}/pixels")]
    public async Task<IActionResult> GetByPixelDto(string canvasName, [FromQuery]PixelDto pixelDto)
    {
        if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
            return Unauthorized("Session-Id header missing or invalid.");

        var userId = _sessionService.GetUserId(sessionId);
        if (userId == null)
            return Unauthorized("Invalid session.");

        var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
            return NotFound("Canvas not found.");
        var pixelDtoFull = new PixelDto
        {
            CanvasId = canvas.Id,
            X = pixelDto.X,
            Y = pixelDto.Y,
        };
        var pixelExtracted = await _repoManager.PixelRepository.GetByPixelDto(pixelDtoFull);
        if (pixelExtracted == null)
            return NotFound("Pixel not found.");
        return Ok(pixelExtracted);
    }

    [HttpGet("owner/{ownerId}")]
    public async Task<IActionResult> GetByOwnerId(Guid ownerId)
    {
        var pixels = await _repoManager.PixelRepository.GetByOwnerIdAsync(ownerId);
        return Ok(pixels);
    }

    [HttpPost("change")]
    public async Task<IActionResult> TryChangePixel(Guid ownerId, [FromBody] PixelDto pixel)
    {
        var result = await _repoManager.PixelRepository.TryChangePixelAsync(ownerId, pixel);
        if (result == null)
            return BadRequest("Could not change pixel.");
        return Ok(result);
    }
}

