using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class UsersController : ControllerBase
{
    private readonly RepositoryManager _repoManager;
    private readonly SessionService _sessionService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(RepositoryManager repoManager, SessionService sessionService, ILogger<UsersController> logger)
    {
        _repoManager = repoManager;
        _sessionService = sessionService;
        _logger = logger;
    }

    [HttpGet("email/{email}")]
    public async Task<IActionResult> GetByEmail(string email)
    {
        var user = await _repoManager.UserRepository.GetByEmailAsync(email);
        if (user == null)
            return NotFound();
        return Ok(user);
    }

    [HttpGet("username/{userName}")]
    public async Task<IActionResult> GetByUserName(string userName)
    {
        var user = await _repoManager.UserRepository.GetByUserNameAsync(userName);
        if (user == null)
            return NotFound();
        return Ok(user);
    }

    [HttpGet("id/{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _repoManager.UserRepository.GetByIdAsync(id);
        if (user == null)
            return NotFound();
        return Ok(user);
    }

    [HttpPost("add-or-update")]
    public async Task<IActionResult> AddOrUpdateUser([FromBody] UserDto userDto, [FromQuery] string? passwordHashOrKey, [FromQuery] int loginMethod = 0)
    {
        var passwordDto = new PasswordDto { PasswordHashOrKey = passwordHashOrKey, LoginMethod = (Linteum.Shared.LoginMethod)loginMethod };
        var result = await _repoManager.UserRepository.AddOrUpdateUserAsync(userDto, passwordDto);
        if (result == null)
            return BadRequest("Could not add or update user.");
        return Ok(result);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteUser([FromBody] UserDto userDto)
    {
        var result = await _repoManager.UserRepository.DeleteUserAsync(userDto);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> TryLogin([FromBody] UserDto userDto, [FromQuery] string? passwordHashOrKey)
    {
        _logger.LogInformation("Login attempt for user: {Email}", userDto.Email);

        var passwordDto = new PasswordDto { PasswordHashOrKey = passwordHashOrKey, LoginMethod = userDto.LoginMethod };
        var result = await _repoManager.UserRepository.TryLogin(userDto, passwordDto);

        if (result == null)
        {
            _logger.LogWarning("Login failed for user: {Email}", userDto.Email);
            return Unauthorized();
        }

        if (!result.Id.HasValue)
        {
            _logger.LogError("Login succeeded but user ID is null for user: {Email}", userDto.Email);
            return BadRequest("Can't retrieve user ID after login.");
        }

        var sessionId = _sessionService.CreateSession(result.Id.Value);
        _logger.LogInformation("Login successful for user: {Email} with ID: {UserId}, session created: {SessionId}", userDto.Email, result.Id.Value, sessionId);

        return Ok(new LoginResponse{ User = result, SessionId = sessionId });
    }
}



