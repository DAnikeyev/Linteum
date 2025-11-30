using Linteum.Api.Configuration;
using Linteum.Shared.Exceptions;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Linteum.Api.Services;
using Linteum.Shared;
using Linteum.Shared.Exceptions;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CanvasesController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;
        private readonly SessionService _sessionService;
        private readonly ILogger<CanvasesController> _logger;
        private readonly CanvasSizeOptions _canvasSizeOptions;

        public CanvasesController(RepositoryManager repoManager, SessionService sessionService, ILogger<CanvasesController> logger, IOptions<CanvasSizeOptions> canvasSizeOptions)
        {
            _logger = logger;
            _canvasSizeOptions = canvasSizeOptions.Value;
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

        private bool IsCanvasSizeValid(CanvasDto canvas)
        {
            return canvas.Width >= _canvasSizeOptions.MinWidth &&
                   canvas.Width <= _canvasSizeOptions.MaxWidth &&
                   canvas.Height >= _canvasSizeOptions.MinHeight &&
                   canvas.Height <= _canvasSizeOptions.MaxHeight;
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

            if (!IsCanvasSizeValid(canvas))
            {
                return BadRequest($"Canvas size must be between {_canvasSizeOptions.MinWidth}x{_canvasSizeOptions.MinHeight} and {_canvasSizeOptions.MaxWidth}x{_canvasSizeOptions.MaxHeight}.");
            }

            canvas.CreatorId = userId.Value;
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

        [HttpPost("subscribe")]
        public async Task<IActionResult> SubscribeToCanvas([FromBody] SubscribeCanvasRequestDto subscribeCanvasRequestDto)
        {
            _logger.LogInformation("Subscribing to canvas with name: {CanvasName}", subscribeCanvasRequestDto.Canvas.Name);
            var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }
            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(subscribeCanvasRequestDto.Canvas.Name);
            if (canvas == null)
            {
                _logger.LogWarning("Canvas with name {CanvasName} not found.", subscribeCanvasRequestDto.Canvas.Name);
                return NotFound("Canvas not found.");
            }

            try
            {
                var subscription = await _repoManager.SubscriptionRepository.Subscribe(userId.Value, canvas.Id, subscribeCanvasRequestDto.Password.PasswordHash);
                return Ok(subscription);
            }
            catch (CanvasNotFoundException ex)
            {
                _logger.LogWarning(ex, "Canvas with name {CanvasName} not found during subscription.", subscribeCanvasRequestDto.Canvas.Name);
                return NotFound("Canvas not found.");
            }
            catch (InvalidCanvasPasswordException ex)
            {
                _logger.LogWarning(ex, "Invalid password provided for canvas with name {CanvasName} during subscription.", subscribeCanvasRequestDto.Canvas.Name);
                return Unauthorized("Invalid password.");
            }
            catch (UserAlreadySubscribedException ex)
            {
                _logger.LogInformation(ex, "User {UserId} is already subscribed to canvas with name {CanvasName}.", userId, subscribeCanvasRequestDto.Canvas.Name);
                return BadRequest("User is already subscribed to this canvas.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing user {UserId} to canvas with name {CanvasName}.", userId, subscribeCanvasRequestDto.Canvas.Name);
                return BadRequest("Could not subscribe to canvas.");
            }
        }

        [HttpPost("unsubscribe")]
        public async Task<IActionResult> UnsubscribeFromCanvas([FromBody] CanvasDto canvasDto)
        {
            _logger.LogInformation("Unsubscribing from canvas with name: {CanvasName}", canvasDto.Name);
            if(canvasDto.Name == new Config().DefaultCanvasName)
                return BadRequest("Cannot unsubscribe from the default canvas.");
            var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }
            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(canvasDto.Name);
            if (canvas == null)
            {
                _logger.LogWarning("Canvas with name {CanvasName} not found.", canvasDto.Name);
                return NotFound("Canvas not found.");
            }

            try
            {
                var unsubscription = await _repoManager.SubscriptionRepository.Unsubscribe(userId.Value, canvas.Id);
                return Ok(unsubscription);
            }
            catch (CanvasNotFoundException ex)
            {
                _logger.LogWarning(ex, "Canvas with name {CanvasName} not found during subscription.", canvasDto.Name);
                return NotFound("Canvas not found.");
            }
            catch (BalanceUpdateException ex)
            {
                _logger.LogWarning(ex, "Balance update failed for user {UserId} during unsubscription from canvas with name {CanvasName}.", userId, canvasDto.Name);
                return BadRequest("Can't nullify balance to unsubscribe from canvas.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing user {UserId} from canvas with name {CanvasName}.", userId, canvasDto.Name);
                return BadRequest("Could not subscribe to canvas.");
            }
        }

        /// <summary>
        /// Is not available now. Deleting Canvases is automatic on a startUp
        /// </summary>
        [HttpDelete("delete/{name}")]
        private async Task<IActionResult> DeleteCanvas(string name, [FromQuery] string passwordHash)
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

            var result = await _repoManager.CanvasRepository.TryDeleteCanvasByName(name);
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
