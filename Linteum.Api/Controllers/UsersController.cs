using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class UsersController : ControllerBase
{
    private readonly RepositoryManager _repoManager;

    public UsersController(RepositoryManager repoManager)
    {
        _repoManager = repoManager;
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
    public async Task<IActionResult> TryLogin([FromBody] UserDto userDto, [FromQuery] string? passwordHashOrKey, [FromQuery] int loginMethod = 0)
    {
        var passwordDto = new PasswordDto { PasswordHashOrKey = passwordHashOrKey, LoginMethod = (Linteum.Shared.LoginMethod)loginMethod };
        var result = await _repoManager.UserRepository.TryLogin(userDto, passwordDto);
        if (result == null)
            return Unauthorized();
        return Ok(result);
    }
}

