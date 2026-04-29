using Linteum.Infrastructure;
using Linteum.Api.Services;
using Linteum.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BalanceChangedEventsController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;
        private readonly ILogger<BalanceChangedEventsController> _logger;
        private readonly SessionService _sessionService;

        public BalanceChangedEventsController(RepositoryManager repoManager, SessionService sessionService, ILogger<BalanceChangedEventsController> logger)
        {
            _repoManager = repoManager;
            _sessionService = sessionService;
            _logger = logger;
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var events = (await _repoManager.BalanceChangedEventRepository.GetByUserIdAsync(userId)).ToList();
            _logger.LogInformation("Balance changed events for user {UserId} returned successfully. Count={Count}", userId, events.Count);
            return Ok(events);
        }

        [HttpGet("user/{userId}/canvas/{canvasId}")]
        public async Task<IActionResult> GetByUserAndCanvasId(Guid userId, Guid canvasId)
        {
            var events = (await _repoManager.BalanceChangedEventRepository.GetByUserAndCanvasIdAsync(userId, canvasId)).ToList();
            _logger.LogInformation("Balance changed events for user {UserId} on canvas {CanvasId} returned successfully. Count={Count}", userId, canvasId, events.Count);
            return Ok(events);
        }

        [HttpGet("current/canvas/{canvasId}")]
        public async Task<IActionResult> GetCurrentForSession(Guid canvasId)
        {
            var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
            if (userId == null)
            {
                _logger.LogWarning("Current balance request failed for canvas {CanvasId}: Session-Id header missing or invalid.", canvasId);
                return Unauthorized("Session-Id header missing or invalid.");
            }

            var events = (await _repoManager.BalanceChangedEventRepository.GetByUserAndCanvasIdAsync(userId.Value, canvasId)).ToList();
            var currentBalance = events
                .OrderByDescending(balanceChangedEvent => balanceChangedEvent.ChangedAt)
                .FirstOrDefault()
                ?.NewBalance ?? 0;
            _logger.LogInformation("Current balance for user {UserId} on canvas {CanvasId} returned successfully. Balance={Balance}", userId.Value, canvasId, currentBalance);
            return Ok(currentBalance);
        }

        [HttpPost("change")]
        public async Task<IActionResult> TryChangeBalance(Guid userId, Guid canvasId, long delta, BalanceChangedReason reason)
        {
            var result = await _repoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(userId, canvasId, delta, reason);
            if (result == null)
            {
                _logger.LogWarning("Balance change failed for user {UserId} on canvas {CanvasId}. Delta={Delta}, Reason={Reason}", userId, canvasId, delta, reason);
                return BadRequest("Insufficient balance or error occurred.");
            }

            _logger.LogInformation("Balance changed successfully for user {UserId} on canvas {CanvasId}. Delta={Delta}, Reason={Reason}, NewBalance={NewBalance}", userId, canvasId, delta, reason, result.NewBalance);
            return Ok(result);
        }
    }
}

