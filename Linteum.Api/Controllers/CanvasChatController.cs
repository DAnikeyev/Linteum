using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class CanvasChatController : ControllerBase
{
    private readonly RepositoryManager _repositoryManager;
    private readonly SessionService _sessionService;
    private readonly ICanvasChatBroadcaster _canvasChatBroadcaster;
    private readonly ILogger<CanvasChatController> _logger;

    public CanvasChatController(
        RepositoryManager repositoryManager,
        SessionService sessionService,
        ICanvasChatBroadcaster canvasChatBroadcaster,
        ILogger<CanvasChatController> logger)
    {
        _repositoryManager = repositoryManager;
        _sessionService = sessionService;
        _canvasChatBroadcaster = canvasChatBroadcaster;
        _logger = logger;
    }

    [HttpPost("{canvasName}")]
    public async Task<IActionResult> SendMessage(string canvasName, [FromBody] SendCanvasChatMessageRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
        if (!userId.HasValue)
        {
            _logger.LogWarning("Canvas chat send failed: Session-Id header missing or invalid.");
            return Unauthorized("Session-Id header missing or invalid.");
        }

        if (string.IsNullOrWhiteSpace(canvasName))
        {
            return BadRequest("Canvas name is required.");
        }

        var normalizedMessage = request.Message.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return BadRequest("Message is required.");
        }

        if (normalizedMessage.Length > SendCanvasChatMessageRequestDto.MaxMessageLength)
        {
            return BadRequest($"Message must be {SendCanvasChatMessageRequestDto.MaxMessageLength} characters or less.");
        }

        var canvas = await _repositoryManager.CanvasRepository.GetByNameAsync(canvasName);
        if (canvas == null)
        {
            _logger.LogWarning("Canvas chat send failed: canvas {CanvasName} not found for user {UserId}.", canvasName, userId.Value);
            return NotFound("Canvas not found.");
        }

        var user = await _repositoryManager.UserRepository.GetByIdAsync(userId.Value);
        if (user == null)
        {
            _logger.LogWarning("Canvas chat send failed: user {UserId} not found for canvas {CanvasName}.", userId.Value, canvas.Name);
            return Unauthorized("Invalid session user.");
        }

        var chatMessage = new CanvasChatMessageDto
        {
            CanvasName = canvas.Name,
            UserName = string.IsNullOrWhiteSpace(user.UserName) ? "Anonymous" : user.UserName,
            Message = normalizedMessage,
            SentAtUtc = DateTime.UtcNow,
        };

        await _canvasChatBroadcaster.BroadcastAsync(canvas.Name, chatMessage, cancellationToken);

        _logger.LogInformation(
            "Canvas chat message accepted for {CanvasName} from user {UserId}. Length={MessageLength}",
            canvas.Name,
            userId.Value,
            normalizedMessage.Length);

        return Accepted();
    }
}


