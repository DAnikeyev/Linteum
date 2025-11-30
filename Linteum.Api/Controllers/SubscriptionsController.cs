using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class SubscriptionsController : ControllerBase
{
    private readonly RepositoryManager _repoManager;
    private readonly ILogger<SubscriptionsController> _logger;

    public SubscriptionsController(RepositoryManager repoManager, ILogger<SubscriptionsController> logger)
    {
        _repoManager = repoManager;
        _logger = logger;
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
}

