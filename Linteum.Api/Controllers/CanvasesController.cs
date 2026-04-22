using Linteum.Api.Configuration;
using Linteum.Shared.Exceptions;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Linteum.Api.Services;
using Linteum.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

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
            var canvases = (await _repoManager.CanvasRepository.GetAllAsync(includePrivate)).ToList();
            _logger.LogInformation("Canvases returned successfully. IncludePrivate={IncludePrivate}, Count={Count}", includePrivate, canvases.Count);
            return Ok(canvases);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var canvases = (await _repoManager.CanvasRepository.GetByUserIdAsync(userId)).ToList();
            _logger.LogInformation("Canvases for user {UserId} returned successfully. Count={Count}", userId, canvases.Count);
            return Ok(canvases);
        }

        [HttpGet("name/{name}")]
        public async Task<IActionResult> GetByName(string name)
        {
            var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }
            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(name);
            if (canvas == null)
            {
                _logger.LogInformation("Canvas lookup for {CanvasName} returned no result for user {UserId}.", name, userId.Value);
                return NotFound();
            }

            _logger.LogInformation("Canvas {CanvasName} returned successfully for user {UserId}.", name, userId.Value);
            return Ok(canvas);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string name, [FromQuery] bool includePrivate = true)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogInformation("Canvas search completed with an empty query. IncludePrivate={IncludePrivate}", includePrivate);
                return Ok(new List<CanvasDto>());
            }
            var canvases = (await _repoManager.CanvasRepository.SearchByNameAsync(name, includePrivate)).ToList();
            _logger.LogInformation("Canvas search for '{Query}' completed successfully. IncludePrivate={IncludePrivate}, Count={Count}", name, includePrivate, canvases.Count);
            return Ok(canvases);
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
            var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }

            if (!IsCanvasSizeValid(canvas))
            {
                _logger.LogWarning("Canvas creation failed for user {UserId}: invalid canvas size {Width}x{Height} for {CanvasName}.", userId.Value, canvas.Width, canvas.Height, canvas.Name);
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
            _logger.LogInformation("Canvas {CanvasName} was created and subscribed successfully for user {UserId}.", canvas.Name, userId.Value);
            return Ok(result);
        }

        [HttpPost("subscribe")]
        public async Task<IActionResult> SubscribeToCanvas([FromBody] SubscribeCanvasRequestDto subscribeCanvasRequestDto)
        {
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
                _logger.LogInformation("User {UserId} subscribed successfully to canvas {CanvasName}.", userId.Value, subscribeCanvasRequestDto.Canvas.Name);
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
                _logger.LogWarning(ex, "User {UserId} is already subscribed to canvas with name {CanvasName}.", userId, subscribeCanvasRequestDto.Canvas.Name);
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
            if(canvasDto.Name == new Config().DefaultCanvasName)
            {
                _logger.LogWarning("Cannot unsubscribe from default canvas {CanvasName}.", canvasDto.Name);
                return BadRequest("Cannot unsubscribe from the default canvas.");
            }
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
                _logger.LogInformation("User {UserId} unsubscribed successfully from canvas {CanvasName}.", userId.Value, canvasDto.Name);
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
            {
                _logger.LogWarning("Canvas deletion failed for {CanvasName} by user {UserId}.", name, userId.Value);
                return BadRequest("Canvas could not be deleted.");
            }
            _logger.LogInformation("Canvas {CanvasName} deleted successfully by user {UserId}.", name, userId.Value);
            return Ok();
        }

        [HttpPost("check-password")]
        public async Task<IActionResult> CheckPassword([FromBody] CanvasDto canvas, [FromQuery] string? passwordHash)
        {
            var result = await _repoManager.CanvasRepository.CheckPassword(canvas, passwordHash);
            _logger.LogInformation("Canvas password check completed for canvas {CanvasId}. Result={Result}", canvas.Id, result);
            return Ok(result);
        }

        [HttpGet("image/{name}")]
        public async Task<IActionResult> GetImage(string name)
        {
            if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
            {
                _logger.LogWarning("Canvas image request failed for {CanvasName}: Session-Id header missing or invalid.", name);
                return Unauthorized("Session-Id header missing or invalid.");
            }

            var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
            if (userId == null)
            {
                _logger.LogWarning("Canvas image request failed for {CanvasName}: invalid session {SessionId}.", name, sessionId);
                return Unauthorized("Invalid session.");
            }

            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(name);
            if (canvas == null)
            {
                _logger.LogWarning("Canvas image request failed: canvas {CanvasName} not found for user {UserId}.", name, userId.Value);
                return NotFound("Canvas not found.");
            }

            var pixels = await _repoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id);
            var colors = await _repoManager.ColorRepository.GetAllAsync();
            var colorMap = colors.ToDictionary(c => c.Id, c => c.HexValue);
            
            using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(canvas.Width, canvas.Height);
            
            image.Mutate(x => x.Fill(Color.White));

            foreach (var pixel in pixels)
            {
                if (colorMap.TryGetValue(pixel.ColorId, out var hexColor))
                {
                    // Parse hex string to Color
                    var color = Color.ParseHex(hexColor);
                    image[pixel.X, pixel.Y] = color;
                }
            }

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            _logger.LogInformation("Canvas image for {CanvasName} generated successfully for user {UserId}.", name, userId.Value);
            return File(ms.ToArray(), "image/png");
        }

        
        [HttpGet("subscribed")]
        public async Task<IActionResult> GetImage()
        {
            if (!Request.Headers.TryGetValue(CustomHeaders.SessionId, out var sessionIdStr) || !Guid.TryParse(sessionIdStr, out var sessionId))
            {
                _logger.LogWarning("Subscribed canvases request failed: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }

            var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
            if (userId == null)
            {
                _logger.LogWarning("Subscribed canvases request failed: invalid session {SessionId}.", sessionId);
                return Unauthorized("Invalid session.");
            }

            var canvases = (await _repoManager.CanvasRepository.GetByUserIdAsync(userId.Value)).ToList();
            _logger.LogInformation("Subscribed canvases returned successfully for user {UserId}. Count={Count}", userId.Value, canvases.Count);
            return Ok(canvases);
        }
    }
}
