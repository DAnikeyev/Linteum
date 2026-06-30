using Linteum.Api.Configuration;
using Linteum.Api.Attributes;
using Linteum.Api.Middleware;
using Linteum.Shared.Exceptions;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Linteum.Api.Services;
using Linteum.Api.Models;
using Linteum.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CanvasesController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;
        private readonly ILogger<CanvasesController> _logger;
        private readonly CanvasSizeOptions _canvasSizeOptions;
        private readonly Config _config;
        private readonly ICanvasSeedQueue _canvasSeedQueue;
        private readonly ICanvasMaintenanceQueue _canvasMaintenanceQueue;
        private readonly ICanvasImageCache _imageCache;
        private const long MaxCanvasImageUploadBytes = 20 * 1024 * 1024;

        public CanvasesController(RepositoryManager repoManager, ILogger<CanvasesController> logger, IOptions<CanvasSizeOptions> canvasSizeOptions, Config config, ICanvasSeedQueue canvasSeedQueue, ICanvasMaintenanceQueue canvasMaintenanceQueue, ICanvasImageCache imageCache)
        {
            _logger = logger;
            _canvasSizeOptions = canvasSizeOptions.Value;
            _repoManager = repoManager;
            _config = config;
            _canvasSeedQueue = canvasSeedQueue;
            _canvasMaintenanceQueue = canvasMaintenanceQueue;
            _imageCache = imageCache;
        }

        [HttpGet]
        [DisabledEndpoint]
        public async Task<IActionResult> GetAll([FromQuery] bool includePrivate = true)
        {
            var canvases = (await _repoManager.CanvasRepository.GetAllAsync(includePrivate)).ToList();
            _logger.LogInformation("Canvases returned successfully. IncludePrivate={IncludePrivate}, Count={Count}", includePrivate, canvases.Count);
            return Ok(canvases);
        }

        [HttpGet("user/{userId}")]
        [DisabledEndpoint]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var canvases = (await _repoManager.CanvasRepository.GetByUserIdAsync(userId)).ToList();
            _logger.LogInformation("Canvases for user {UserId} returned successfully. Count={Count}", userId, canvases.Count);
            return Ok(canvases);
        }

        [HttpGet("name/{name}")]
        public async Task<IActionResult> GetByName(string name)
        {
            var userId = HttpContext.GetSessionUserId();
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

            if (!await CanAccessCanvasAsync(userId.Value, canvas))
            {
                _logger.LogWarning("User {UserId} attempted to access password-protected canvas {CanvasName} without a subscription.", userId.Value, name);
                return Unauthorized("Password required.");
            }

            await TryAutoSubscribeAsync(userId.Value, canvas);

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
        public async Task<IActionResult> AddCanvas([FromBody] CreateCanvasRequestDto request)
        {
            var userId = HttpContext.GetSessionUserId();
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }

            var canvas = request.Canvas;

            if (await IsGuestUserAsync(userId.Value))
            {
                _logger.LogWarning("Guest user {UserId} attempted to create canvas {CanvasName}.", userId.Value, canvas.Name);
                return BadRequest("Guest accounts cannot create canvases.");
            }

            if (!IsCanvasSizeValid(canvas))
            {
                _logger.LogWarning("Canvas creation failed for user {UserId}: invalid canvas size {Width}x{Height} for {CanvasName}.", userId.Value, canvas.Width, canvas.Height, canvas.Name);
                return BadRequest($"Canvas size must be between {_canvasSizeOptions.MinWidth}x{_canvasSizeOptions.MinHeight} and {_canvasSizeOptions.MaxWidth}x{_canvasSizeOptions.MaxHeight}.");
            }

            var existingCanvas = await _repoManager.CanvasRepository.GetByNameAsync(canvas.Name);
            if (existingCanvas != null)
            {
                _logger.LogWarning("Canvas creation failed for user {UserId}: canvas {CanvasName} already exists.", userId.Value, canvas.Name);
                return Conflict("A canvas with this name already exists.");
            }

            canvas.CreatorId = userId.Value;
            var result = await _repoManager.CanvasRepository.TryAddCanvas(canvas, request.Password);
            if (result == null)
            {
                _logger.LogError("Canvas creation failed for user {UserId} with name {CanvasName}", userId, canvas.Name);
                return BadRequest("Canvas could not be created.");
            }
            var sub = await _repoManager.SubscriptionRepository.Subscribe(userId.Value, result.Id, request.Password);
            if (sub == null)
            {
                _logger.LogError("Subscription failed for user {UserId} on canvas {CanvasName}", userId, canvas.Name);
                return BadRequest("Canvas could not be subscribed to.");
            }
            _logger.LogInformation("Canvas {CanvasName} was created and subscribed successfully for user {UserId}.", canvas.Name, userId.Value);
            return Ok(result);
        }

        [HttpPost("add-with-image")]
        public async Task<IActionResult> AddCanvasWithImage([FromForm] CanvasImageUploadForm request)
        {
            var userId = HttpContext.GetSessionUserId();
            if (userId == null)
            {
                _logger.LogWarning("Unauthorized access attempt: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }

            if (await IsGuestUserAsync(userId.Value))
            {
                _logger.LogWarning("Guest user {UserId} attempted to create image-backed canvas {CanvasName}.", userId.Value, request.Name);
                return BadRequest("Guest accounts cannot create canvases.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Canvas name cannot be empty.");
            }

            if (request.Image == null || request.Image.Length == 0)
            {
                return BadRequest("A JPG image is required.");
            }

            if (request.Image.Length > MaxCanvasImageUploadBytes)
            {
                return BadRequest("Image size must be 20MB or less.");
            }

            byte[] imageBytes;
            IImageFormat? imageFormat;
            ImageInfo? imageInfo;

            try
            {
                await using var uploadStream = request.Image.OpenReadStream();
                using var memoryStream = new MemoryStream();
                await uploadStream.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();

                await using var imageStream = new MemoryStream(imageBytes, writable: false);
                imageFormat = await Image.DetectFormatAsync(imageStream);
                imageStream.Position = 0;
                imageInfo = await Image.IdentifyAsync(imageStream);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Canvas image upload exceeded the allowed size for user {UserId}.", userId.Value);
                return BadRequest("Image size must be 20MB or less.");
            }
            catch (SixLabors.ImageSharp.UnknownImageFormatException ex)
            {
                _logger.LogWarning(ex, "Canvas image upload could not be decoded for user {UserId}.", userId.Value);
                return BadRequest("Only JPG images are supported.");
            }

            if (imageFormat == null)
            {
                _logger.LogWarning("Canvas image upload format could not be detected for user {UserId}.", userId.Value);
                return BadRequest("Only JPG images are supported.");
            }

            if (imageInfo == null)
            {
                _logger.LogWarning("Canvas image upload dimensions could not be determined for user {UserId}.", userId.Value);
                return BadRequest("Uploaded image could not be read.");
            }

            if (!string.Equals(imageFormat.Name, JpegFormat.Instance.Name, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Only JPG images are supported.");
            }

            var canvas = new CanvasDto
            {
                Name = request.Name.Trim(),
                Width = imageInfo.Width,
                Height = imageInfo.Height,
                CanvasMode = request.CanvasMode,
                CreatorId = userId.Value,
            };

            if (!IsCanvasSizeValid(canvas))
            {
                _logger.LogWarning("Canvas image upload failed for user {UserId}: invalid canvas size {Width}x{Height} for {CanvasName}.", userId.Value, canvas.Width, canvas.Height, canvas.Name);
                return BadRequest($"Canvas size must be between {_canvasSizeOptions.MinWidth}x{_canvasSizeOptions.MinHeight} and {_canvasSizeOptions.MaxWidth}x{_canvasSizeOptions.MaxHeight}.");
            }

            var existingCanvas = await _repoManager.CanvasRepository.GetByNameAsync(canvas.Name);
            if (existingCanvas != null)
            {
                _logger.LogWarning("Canvas image upload failed for user {UserId}: canvas {CanvasName} already exists.", userId.Value, canvas.Name);
                return Conflict("A canvas with this name already exists.");
            }

            var result = await _repoManager.CanvasRepository.TryAddCanvas(canvas, request.Password);
            if (result == null)
            {
                _logger.LogError("Canvas creation from image failed for user {UserId} with name {CanvasName}", userId, canvas.Name);
                return BadRequest("Canvas could not be created.");
            }

            var sub = await _repoManager.SubscriptionRepository.Subscribe(userId.Value, result.Id, request.Password);
            if (sub == null)
            {
                _logger.LogError("Subscription failed for user {UserId} on canvas {CanvasName}", userId, canvas.Name);
                return BadRequest("Canvas could not be subscribed to.");
            }

            var creator = await _repoManager.UserRepository.GetByIdAsync(userId.Value);
            await _canvasSeedQueue.QueueAsync(new QueuedCanvasSeedRequest(
                userId.Value,
                creator?.UserName ?? string.Empty,
                result.Id,
                result.Name,
                result.CanvasMode,
                result.Width,
                result.Height,
                imageBytes));

            _logger.LogInformation("Canvas {CanvasName} was created from image and queued for seeding successfully for user {UserId}.", canvas.Name, userId.Value);
            return Ok(result);
        }

        [HttpPost("subscribe")]
        public async Task<IActionResult> SubscribeToCanvas([FromBody] SubscribeCanvasRequestDto subscribeCanvasRequestDto)
        {
            var userId = HttpContext.GetSessionUserId();
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
                var subscription = await _repoManager.SubscriptionRepository.Subscribe(userId.Value, canvas.Id, subscribeCanvasRequestDto.Password);
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
            if (_config.GetNonUnsubscribableCanvasNames().Contains(canvasDto.Name, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Cannot unsubscribe from required canvas {CanvasName}.", canvasDto.Name);
                return BadRequest("Cannot unsubscribe from a built-in canvas.");
            }
            var userId = HttpContext.GetSessionUserId();
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

        [HttpPost("erase/{name}")]
        public async Task<IActionResult> EraseCanvas(string name)
        {
            _logger.LogInformation("EraseCanvas requested for {CanvasName}.", name);
            var userId = HttpContext.GetSessionUserId();
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

            if (_config.GetProtectedCanvasNames().Contains(canvas.Name, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("User {UserId} attempted to erase protected canvas {CanvasName}.", userId.Value, name);
                return BadRequest("Cannot erase a built-in canvas.");
            }

            var canEraseCanvas = canvas.CreatorId == userId.Value || canvas.CanvasMode == CanvasMode.FreeDraw;
            if (!canEraseCanvas)
            {
                _logger.LogWarning("Forbidden erase attempt for canvas {CanvasName} by user {UserId}. CreatorId={CreatorId}, CanvasMode={CanvasMode}", name, userId.Value, canvas.CreatorId, canvas.CanvasMode);
                return StatusCode(StatusCodes.Status403Forbidden, "Only the canvas creator can erase this canvas unless it is FreeDraw.");
            }

            var queueResult = await _canvasMaintenanceQueue.QueueEraseAsync(new QueuedCanvasMaintenanceRequest(
                userId.Value,
                canvas.Id,
                canvas.Name,
                userId.Value.ToString(),
                DateTime.UtcNow));

            _logger.LogInformation(
                "Canvas erase queued for {CanvasName} by user {UserId}. CanvasId={CanvasId}, Mode={CanvasMode}, CreatorId={CreatorId}, AlreadyQueued={AlreadyQueued}",
                canvas.Name,
                userId.Value,
                canvas.Id,
                canvas.CanvasMode,
                canvas.CreatorId,
                queueResult.AlreadyQueued);

            return Accepted(new CanvasOperationResponseDto
            {
                Completed = false,
                Queued = true,
                Message = queueResult.AlreadyQueued
                    ? $"Canvas {canvas.Name} erase is already queued."
                    : $"Canvas {canvas.Name} erase was queued and will finish in the background.",
            });
        }

        [HttpDelete("delete/{name}")]
        public async Task<IActionResult> DeleteCanvas(string name)
        {
            _logger.LogInformation("DeleteCanvas requested for {CanvasName}.", name);
            var userId = HttpContext.GetSessionUserId();
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

            if (_config.GetProtectedCanvasNames().Contains(canvas.Name, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("User {UserId} attempted to delete protected canvas {CanvasName}.", userId.Value, name);
                return BadRequest("Cannot delete a built-in canvas.");
            }

            if (canvas.CreatorId != userId.Value)
            {
                _logger.LogWarning("Forbidden deletion attempt for canvas {CanvasName} by user {UserId}. CreatorId={CreatorId}", name, userId.Value, canvas.CreatorId);
                return StatusCode(StatusCodes.Status403Forbidden, "Only the canvas creator can delete this canvas.");
            }

            var queueResult = await _canvasMaintenanceQueue.QueueDeleteAsync(new QueuedCanvasMaintenanceRequest(
                userId.Value,
                canvas.Id,
                canvas.Name,
                userId.Value.ToString(),
                DateTime.UtcNow));

            _logger.LogInformation(
                "Canvas deletion queued for {CanvasName} by user {UserId}. CanvasId={CanvasId}, CreatorId={CreatorId}, AlreadyQueued={AlreadyQueued}",
                canvas.Name,
                userId.Value,
                canvas.Id,
                canvas.CreatorId,
                queueResult.AlreadyQueued);

            return Accepted(new CanvasOperationResponseDto
            {
                Completed = false,
                Queued = true,
                Message = queueResult.AlreadyQueued
                    ? $"Canvas {canvas.Name} deletion is already queued."
                    : $"Canvas {canvas.Name} deletion was queued and will finish in the background.",
            });
        }

        [HttpPost("check-password")]
        [DisabledEndpoint]
        public async Task<IActionResult> CheckPassword([FromBody] CanvasDto canvas, [FromQuery] string? passwordHash)
        {
            var result = await _repoManager.CanvasRepository.CheckPassword(canvas, passwordHash);
            _logger.LogInformation("Canvas password check completed for canvas {CanvasId}. Result={Result}", canvas.Id, result);
            return Ok(result);
        }

        [HttpGet("image/{name}")]
        public async Task<IActionResult> GetImage(string name)
        {
            var userId = HttpContext.GetSessionUserId();
            if (userId == null)
            {
                _logger.LogWarning("Canvas image request failed for {CanvasName}: Session-Id header missing or invalid.", name);
                return Unauthorized("Session-Id header missing or invalid.");
            }

            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(name);
            if (canvas == null)
            {
                _logger.LogWarning("Canvas image request failed: canvas {CanvasName} not found for user {UserId}.", name, userId.Value);
                return NotFound("Canvas not found.");
            }

            if (!await CanAccessCanvasAsync(userId.Value, canvas))
            {
                _logger.LogWarning("User {UserId} requested image of password-protected canvas {CanvasName} without a subscription.", userId.Value, name);
                return Unauthorized("Password required.");
            }

            // Served from the in-memory raster cache (cold-rendered from the DB on first access, then
            // kept live by write-through on pixel changes). See CanvasImageCache.
            var cached = await _imageCache.GetOrRenderAsync(canvas.Id, canvas.Name, canvas.Width, canvas.Height, HttpContext.RequestAborted);
            _logger.LogDebug("Canvas image for {CanvasName} served from cache for user {UserId}.", name, userId.Value);
            return File(cached.Bytes, cached.ContentType);
        }

        
        [HttpGet("subscribed")]
        public async Task<IActionResult> GetImage()
        {
            var userId = HttpContext.GetSessionUserId();
            if (userId == null)
            {
                _logger.LogWarning("Subscribed canvases request failed: Session-Id header missing or invalid.");
                return Unauthorized("Session-Id header missing or invalid.");
            }

            var canvases = (await _repoManager.CanvasRepository.GetByUserIdAsync(userId.Value)).ToList();
            _logger.LogInformation("Subscribed canvases returned successfully for user {UserId}. Count={Count}", userId.Value, canvases.Count);
            return Ok(canvases);
        }

        private async Task<bool> IsGuestUserAsync(Guid userId)
        {
            var user = await _repoManager.UserRepository.GetByIdAsync(userId);
            return GuestUserHelper.IsGuest(user);
        }

        /// <summary>
        /// A user may access a canvas's content if it is public, or if they hold an active
        /// subscription (which proves they supplied the password at least once). Used to gate
        /// read paths so a password-protected canvas can't be viewed via a direct link without
        /// the password.
        /// </summary>
        private async Task<bool> CanAccessCanvasAsync(Guid userId, CanvasDto canvas)
        {
            return !canvas.IsPasswordProtected
                || await _repoManager.SubscriptionRepository.IsSubscribedAsync(userId, canvas.Id);
        }

        private async Task TryAutoSubscribeAsync(Guid userId, CanvasDto canvas)
        {
            if (canvas.IsPasswordProtected)
            {
                return;
            }

            var existingSubscriptions = await _repoManager.SubscriptionRepository.GetByUserIdAsync(userId);
            if (existingSubscriptions.Any(subscription => subscription.CanvasId == canvas.Id))
            {
                _logger.LogDebug("Auto-subscribe skipped for user {UserId} on canvas {CanvasName} because the subscription already exists.", userId, canvas.Name);
                return;
            }

            try
            {
                await _repoManager.SubscriptionRepository.Subscribe(userId, canvas.Id, null);
                _logger.LogInformation("Auto-subscribed user {UserId} to public canvas {CanvasName}.", userId, canvas.Name);
            }
            catch (UserAlreadySubscribedException)
            {
                _logger.LogDebug("Auto-subscribe skipped for user {UserId} on canvas {CanvasName} because the subscription already exists.", userId, canvas.Name);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Auto-subscribe skipped for user {UserId} on canvas {CanvasName}.", userId, canvas.Name);
            }
        }
    }
}
