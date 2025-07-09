using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared;
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
    public async Task<IActionResult> Login([FromBody] UserDto userDto, [FromQuery] string? passwordHashOrKey)
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

        return Ok(new LoginResponse { User = result, SessionId = sessionId });
    }

    [HttpPost("changeName")]
    public async Task<IActionResult> ChangeUsername([FromBody] UserDto userDto)
    {
        _logger.LogInformation("Username change requested for user with username: {Username}", userDto.UserName);
        
        var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
        if (!userId.HasValue)
        {
            _logger.LogWarning("Unauthorized username change attempt");
            return Unauthorized();
        }

        var currentUser = await _repoManager.UserRepository.GetByIdAsync(userId.Value);
        if (currentUser == null)
        {
            _logger.LogWarning("User not found for ID: {UserId}", userId.Value);
            return NotFound();
        }

        // Update only the username
        currentUser.UserName = userDto.UserName;

        var result = await _repoManager.UserRepository.AddOrUpdateUserAsync(currentUser);
        if (result == null)
        {
            _logger.LogError("Failed to update username for user ID: {UserId}", userId.Value);
            return BadRequest("Could not update username.");
        }

        _logger.LogInformation("Username updated successfully for user ID: {UserId}", userId.Value);
        return Ok(result);
    }

    [HttpPost("changePassword")]
    public async Task<IActionResult> ChangePassword([FromQuery] string passwordHashOrKey, [FromQuery] int loginMethod = (int)LoginMethod.Password)
    {
        _logger.LogInformation("Password change requested for user with login method: {LoginMethod}", (Linteum.Shared.LoginMethod)loginMethod);
        var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
        if (!userId.HasValue)
        {
            _logger.LogWarning("Unauthorized username change attempt");
            return Unauthorized();
        }

        _logger.LogInformation("Password change requested for user ID: {UserId}", userId.Value);

        var user = await _repoManager.UserRepository.GetByIdAsync(userId.Value);
        if (user == null)
        {
            _logger.LogWarning("User not found for ID: {UserId}", userId.Value);
            return NotFound();
        }

        var passwordDto = new PasswordDto
        {
            PasswordHashOrKey = passwordHashOrKey,
            LoginMethod = (Linteum.Shared.LoginMethod)loginMethod
        };

        var result = await _repoManager.UserRepository.AddOrUpdateUserAsync(user, passwordDto);
        if (result == null)
        {
            _logger.LogError("Failed to update password for user ID: {UserId}", userId.Value);
            return BadRequest("Could not update password.");
        }

        _logger.LogInformation("Password updated successfully for user ID: {UserId}", userId.Value);
        return Ok();
    }
}



