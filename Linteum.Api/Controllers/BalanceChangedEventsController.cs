using Linteum.Infrastructure;
using Linteum.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BalanceChangedEventsController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;

        public BalanceChangedEventsController(RepositoryManager repoManager)
        {
            _repoManager = repoManager;
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var events = await _repoManager.BalanceChangedEventRepository.GetByUserIdAsync(userId);
            return Ok(events);
        }

        [HttpGet("user/{userId}/canvas/{canvasId}")]
        public async Task<IActionResult> GetByUserAndCanvasId(Guid userId, Guid canvasId)
        {
            var events = await _repoManager.BalanceChangedEventRepository.GetByUserAndCanvasIdAsync(userId, canvasId);
            return Ok(events);
        }

        [HttpPost("change")]
        public async Task<IActionResult> TryChangeBalance(Guid userId, Guid canvasId, long delta, BalanceChangedReason reason)
        {
            var result = await _repoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(userId, canvasId, delta, reason);
            if (result == null)
                return BadRequest("Insufficient balance or error occurred.");
            return Ok(result);
        }
    }
}

