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
        var events = await _repoManager.LoginEventRepository.GetByUserIdAsync(userId);
        return Ok(events);
    }

    [HttpPost]
    public async Task<IActionResult> AddLoginEvent([FromBody] LoginEventDto loginEventDto)
    {
        var result = await _repoManager.LoginEventRepository.AddLoginEvent(loginEventDto);
        if (!result)
            return BadRequest("Could not add login event.");
        return Ok();
    }
}

