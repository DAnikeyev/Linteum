using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class SubscriptionsController : ControllerBase
{
    private readonly RepositoryManager _repoManager;

    public SubscriptionsController(RepositoryManager repoManager)
    {
        _repoManager = repoManager;
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUserId(Guid userId)
    {
        var subs = await _repoManager.SubscriptionRepository.GetByUserIdAsync(userId);
        return Ok(subs);
    }

    [HttpGet("canvas/{canvasId}")]
    public async Task<IActionResult> GetByCanvasId(Guid canvasId)
    {
        var subs = await _repoManager.SubscriptionRepository.GetByCanvasIdAsync(canvasId);
        return Ok(subs);
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(Guid userId, Guid canvasId, string? passwordHash)
    {
        var result = await _repoManager.SubscriptionRepository.Subscribe(userId, canvasId, passwordHash);
        if (result == null)
            return BadRequest("Could not subscribe.");
        return Ok(result);
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe(Guid userId, Guid canvasId)
    {
        var result = await _repoManager.SubscriptionRepository.Unsubscribe(userId, canvasId);
        if (result == null)
            return BadRequest("Could not unsubscribe.");
        return Ok(result);
    }
}

