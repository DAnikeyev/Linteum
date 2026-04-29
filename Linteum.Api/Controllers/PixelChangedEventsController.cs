using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PixelChangedEventsController : ControllerBase
{
    private readonly RepositoryManager _repoManager;
    private readonly ILogger<PixelChangedEventsController> _logger;

    public PixelChangedEventsController(RepositoryManager repoManager, ILogger<PixelChangedEventsController> logger)
    {
        _repoManager = repoManager;
        _logger = logger;
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUserId(Guid userId)
    {
        var events = (await _repoManager.PixelChangedEventRepository.GetByUserIdAsync(userId)).ToList();
        _logger.LogInformation("Pixel changed events for user {UserId} returned successfully. Count={Count}", userId, events.Count);
        return Ok(events);
    }

    [HttpGet("pixel/{pixelId}")]
    public async Task<IActionResult> GetByPixelId(Guid pixelId)
    {
        // Currently stores only 10 last entries, so no big data transfers here.
        var events = (await _repoManager.PixelChangedEventRepository.GetByPixelIdAsync(pixelId)).ToList();
        var userNames = await _repoManager.UserRepository.GetByIdAsync(events.Select(x => x.OwnerUserId).ToList());

        var response = events.Zip(userNames, (evt, user) => new HistoryResponseItem()
        {
            UserName = user, NewColorId = evt.NewColorId, OldColorId = evt.OldColorId, Timestamp = evt.ChangedAt,
        }).ToList();
        _logger.LogInformation("Pixel history for pixel {PixelId} returned successfully. Count={Count}", pixelId, response.Count);
        return Ok(response);
    }

    [HttpGet("canvas/{canvasId}")]
    public async Task<IActionResult> GetByCanvasId(Guid canvasId, [FromQuery] DateTime? startDate, [FromQuery] int limit = 1000)
    {
        // Limit to 10000 to prevent OOM on large canvases/active history
        var safeLimit = Math.Clamp(limit, 1, 10000);
        var events = (await _repoManager.PixelChangedEventRepository.GetByCanvasIdAsync(canvasId, startDate))
            .Take(safeLimit)
            .ToList();
        _logger.LogInformation("Pixel changed events for canvas {CanvasId} returned successfully. Count={Count}, StartDate={StartDate}, Limit={Limit}", canvasId, events.Count, startDate, safeLimit);
        return Ok(events);
    }

    [HttpPost]
    public async Task<IActionResult> AddPixelChangedEvent([FromBody] PixelChangedEventDto pixelChangedEventDto)
    {
        var result = await _repoManager.PixelChangedEventRepository.AddPixelChangedEvent(pixelChangedEventDto);
        if (!result)
        {
            _logger.LogWarning("Pixel changed event could not be added for pixel {PixelId}.", pixelChangedEventDto.PixelId);
            return BadRequest("Could not add pixel changed event.");
        }

        _logger.LogInformation("Pixel changed event added successfully for pixel {PixelId}.", pixelChangedEventDto.PixelId);
        return Ok();
    }
}

