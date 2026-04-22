using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class LoginEventsController : ControllerBase
{
    private readonly RepositoryManager _repoManager;
    private readonly ILogger<LoginEventsController> _logger;

    public LoginEventsController(RepositoryManager repoManager, ILogger<LoginEventsController> logger)
    {
        _repoManager = repoManager;
        _logger = logger;
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUserId(Guid userId)
    {
        var events = (await _repoManager.LoginEventRepository.GetByUserIdAsync(userId)).ToList();
        _logger.LogInformation("Login events for user {UserId} returned successfully. Count={Count}", userId, events.Count);
        return Ok(events);
    }

    [HttpPost]
    public async Task<IActionResult> AddLoginEvent([FromBody] LoginEventDto loginEventDto)
    {
        var result = await _repoManager.LoginEventRepository.AddLoginEvent(loginEventDto);
        if (!result)
        {
            _logger.LogWarning("Login event could not be added for user {UserId}.", loginEventDto.UserId);
            return BadRequest("Could not add login event.");
        }

        _logger.LogInformation("Login event added successfully for user {UserId}.", loginEventDto.UserId);
        return Ok();
    }
}

