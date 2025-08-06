using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;
using Linteum.Api.Services;
using Linteum.Shared;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CanvasesController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;
        private readonly SessionService _sessionService;
        private readonly ILogger<CanvasesController> _logger;

        public CanvasesController(RepositoryManager repoManager, SessionService sessionService, ILogger<CanvasesController> logger)
        {
            _logger = logger;
            _repoManager = repoManager;
            _sessionService = sessionService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includePrivate = true)
        {
            var canvases = await _repoManager.CanvasRepository.GetAllAsync(includePrivate);
            return Ok(canvases);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var canvases = await _repoManager.CanvasRepository.GetByUserIdAsync(userId);
            return Ok(canvases);
        }

        [HttpGet("name/{name}")]
        public async Task<IActionResult> GetByName(string name)
        {
            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(name);
            if (canvas == null)
                return NotFound();
            return Ok(canvas);
        }

        [HttpPost("Add")]
        public async Task<IActionResult> AddCanvas([FromBody] CanvasDto canvas, [FromQuery] string? passwordHash)
        {
            _logger.LogInformation("Adding canvas with name: {CanvasName}", canvas.Name);
            var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }
            var result = await _repoManager.CanvasRepository.TryAddCanvas(canvas, passwordHash);
            if (result == null)
            {
                _logger.LogError("Canvas creation failed for user {UserId} with name {CanvasName}", userId, canvas.Name);
                return BadRequest("Canvas could not be created.");
            }
            var sub = await _repoManager.SubscriptionRepository.Subscribe(userId.Value, result.Id, passwordHash);
            if (sub == null)
            {
                _logger.LogError("Subscription failed for user {UserId} on canvas {CanvasName}", userId, canvas.Name);
                return BadRequest("Canvas could not be subscribed to.");
            }
            return Ok(result);
        }

        [HttpDelete("name/{name}")]
        public async Task<IActionResult> DeleteCanvas(string name, [FromQuery] string passwordHash)
        {
            
            _logger.LogInformation("Trying to delete canvas with name: {CanvasName}", name);
            var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }
            
            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(name);
            if (canvas == null)
            {
                _logger.LogWarning("Canvas with name {CanvasName} not found.", name);
                return NotFound("Canvas not found.");
            }

            if (canvas.CreatorId != userId)
            {
                _logger.LogWarning("Unauthorized deletion attempt for canvas with name {CanvasName} by user {UserId}.", name, userId);
                return Unauthorized("You are not authorized to delete this canvas.");
            }

            if (!await _repoManager.CanvasRepository.CheckPassword(canvas, passwordHash))
            {
                _logger.LogWarning("Password check failed for canvas with name {CanvasName}.", name);
                return Unauthorized("Invalid password.");
            }

            var result = await _repoManager.CanvasRepository.TryDeleteCanvasByName(name, passwordHash);
            if (!result)
                return BadRequest("Canvas could not be deleted.");
            return Ok();
        }

        [HttpPost("check-password")]
        public async Task<IActionResult> CheckPassword([FromBody] CanvasDto canvas, [FromQuery] string? passwordHash)
        {
            var result = await _repoManager.CanvasRepository.CheckPassword(canvas, passwordHash);
            return Ok(result);
        }

        [HttpGet("subscribed")]
        public async Task<IActionResult> GetSubscribedCanvases()
        {
            if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
                return Unauthorized("Session-Id header missing or invalid.");

            var userId = _sessionService.GetUserId(sessionId);
            if (userId == null)
                return Unauthorized("Invalid session.");

            var canvases = await _repoManager.CanvasRepository.GetByUserIdAsync(userId.Value);
            return Ok(canvases);
        }
    }
}
