using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PixelsController : ControllerBase
{
    private readonly RepositoryManager _repoManager;

    public PixelsController(RepositoryManager repoManager)
    {
        _repoManager = repoManager;
    }

    [HttpGet("canvas/{canvasId}")]
    public async Task<IActionResult> GetByCanvasId(Guid canvasId)
    {
        var pixels = await _repoManager.PixelRepository.GetByCanvasIdAsync(canvasId);
        return Ok(pixels);
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

