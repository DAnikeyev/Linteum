using System.Globalization;
using Google.Apis.Auth;
using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Linteum.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class UsersController : ControllerBase
{
    private readonly RepositoryManager _repoManager;
    private readonly SessionService _sessionService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        RepositoryManager repoManager,
        SessionService sessionService,
        IHttpClientFactory httpClientFactory,
        ILogger<UsersController> logger)
    {
        _repoManager = repoManager;
        _sessionService = sessionService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("email/{email}")]
    public async Task<IActionResult> GetByEmail(string email)
    {
        var user = await _repoManager.UserRepository.GetByEmailAsync(email);
        if (user == null)
        {
            _logger.LogInformation("User lookup by email {Email} returned no result.", email);
            return NotFound();
        }

        _logger.LogInformation("User lookup by email {Email} succeeded.", email);
        return Ok(user);
    }

    [HttpGet("username/{userName}")]
    public async Task<IActionResult> GetByUserName(string userName)
    {
        var user = await _repoManager.UserRepository.GetByUserNameAsync(userName);
        if (user == null)
        {
            _logger.LogInformation("User lookup by username {UserName} returned no result.", userName);
            return NotFound();
        }

        _logger.LogInformation("User lookup by username {UserName} succeeded.", userName);
        return Ok(user);
    }

    [HttpGet("id/{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _repoManager.UserRepository.GetByIdAsync(id);
        if (user == null)
        {
            _logger.LogInformation("User lookup by id {UserId} returned no result.", id);
            return NotFound();
        }

        _logger.LogInformation("User lookup by id {UserId} succeeded.", id);
        return Ok(user);
    }

    [HttpPost("add-or-update")]
    public async Task<IActionResult> AddOrUpdateUser([FromBody] UserDto userDto, [FromQuery] string? passwordHashOrKey, [FromQuery] int loginMethod = 0)
    {
        var passwordDto = new UserPaswordDto { PasswordHashOrKey = passwordHashOrKey, LoginMethod = (LoginMethod)loginMethod };
        var result = await _repoManager.UserRepository.AddOrUpdateUserAsync(userDto, passwordDto);
        if (result == null)
        {
            _logger.LogWarning("AddOrUpdateUser failed for {Email}.", userDto.Email);
            return BadRequest("Could not add or update user.");
        }

        _logger.LogInformation("User {Email} was added or updated successfully.", result.Email);
        return Ok(result);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteUser([FromBody] UserDto userDto)
    {
        var result = await _repoManager.UserRepository.DeleteUserAsync(userDto);
        if (result == null)
        {
            _logger.LogWarning("DeleteUser failed: user {UserId} was not found.", userDto.Id);
            return NotFound();
        }

        _logger.LogInformation("User {UserId} was deleted successfully.", result.Id);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] UserDto userDto, [FromQuery] string? passwordHashOrKey)
    {
        var passwordDto = new UserPaswordDto { PasswordHashOrKey = passwordHashOrKey, LoginMethod = userDto.LoginMethod };
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
        await TryAddLoginEventAsync(result.Id.Value, userDto.LoginMethod);

        _logger.LogInformation("Login succeeded for user {Email}. UserId={UserId}, SessionId={SessionId}", userDto.Email, result.Id.Value, sessionId);

        return Ok(new LoginResponse { User = result, SessionId = sessionId });
    }

    [HttpPost("login-google-code")]
    public async Task<IActionResult> LoginWithGoogleCode([FromBody] GoogleLoginCodeRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest("Google authorization code is required.");
        }

        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogError("Google login failed: GOOGLE_CLIENT_ID or GOOGLE_CLIENT_SECRET is not configured.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Google login is not configured.");
        }

        var httpClient = _httpClientFactory.CreateClient();
        var tokenExchangeResponse = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = request.Code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = "postmessage",
                ["grant_type"] = "authorization_code",
            }));

        if (!tokenExchangeResponse.IsSuccessStatusCode)
        {
            var responseBody = await tokenExchangeResponse.Content.ReadAsStringAsync();
            _logger.LogWarning(
                "Google token exchange failed with status {StatusCode}: {ResponseBody}",
                tokenExchangeResponse.StatusCode,
                responseBody);
            return Unauthorized("Google authorization code is invalid or expired.");
        }

        var tokenPayload = await tokenExchangeResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>();
        if (string.IsNullOrWhiteSpace(tokenPayload?.IdToken))
        {
            _logger.LogWarning("Google token exchange succeeded but id_token is missing.");
            return Unauthorized("Google login failed.");
        }

        return await LoginWithGoogleIdTokenAsync(tokenPayload.IdToken, clientId);
    }

    [HttpPost("login-google")]
    public async Task<IActionResult> LoginWithGoogle([FromBody] GoogleLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return BadRequest("Google token is required.");
        }

        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogError("Google login failed: GOOGLE_CLIENT_ID is not configured.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Google login is not configured.");
        }

        return await LoginWithGoogleIdTokenAsync(request.IdToken, clientId);
    }
    
    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] Guid sessionId)
    {
        var userId = _sessionService.GetUserIdAndUpdateTimeLimit(sessionId);
        if (!userId.HasValue)
        {
            _logger.LogWarning("Invalid or expired session: {SessionId}", sessionId);
            return Unauthorized();
        }

        var user = await _repoManager.UserRepository.GetByIdAsync(userId.Value);
        if (user == null)
        {
            _logger.LogError("User not found for ID: {UserId}", userId.Value);
            return NotFound("User not found.");
        }

        _logger.LogInformation("Session {SessionId} validated successfully for user {Email} ({UserId}).", sessionId, user.Email, userId.Value);
        return Ok(new LoginResponse { User = user, SessionId = sessionId });
    }
    
    [HttpPost("add")]
    public async Task<IActionResult> Add([FromBody] UserDto userDto, [FromQuery] string? passwordHashOrKey)
    {
        var passwordDto = new UserPaswordDto { PasswordHashOrKey = passwordHashOrKey, LoginMethod = userDto.LoginMethod };
        var userWithEmail = await _repoManager.UserRepository.GetByEmailAsync(userDto.Email);
        
        if(userDto.UserName is null || userDto.UserName.Length < 4)
        {
            _logger.LogWarning("Invalid username length for user: {Email}", userDto.Email);
            return BadRequest("Username must be at least 4 characters long.");
        }
        
        if (userWithEmail != null)
        {
            _logger.LogWarning("User already exists: {Email}", userDto.Email);
            return BadRequest("User already exists.");
        }
        
        var userWithUserName = await _repoManager.UserRepository.GetByUserNameAsync(userDto.UserName);
        
        if (userWithUserName != null)
        {
            _logger.LogWarning("Username already taken: {UserName}", userDto.UserName);
            return BadRequest("Username already taken.");
        }
        var result = await _repoManager.UserRepository.AddOrUpdateUserAsync(userDto, passwordDto);
        
        if (result == null)
        {
            _logger.LogError("Failed to create user: {Email}", userDto.Email);
            return BadRequest("Could not create user.");
        }
        
        var sessionId = _sessionService.CreateSession(result.Id!.Value);
        _logger.LogInformation("Sign up succeeded for user {Email}. UserId={UserId}, SessionId={SessionId}", result.Email, result.Id.Value, sessionId);

        return Ok(new LoginResponse { User = result, SessionId = sessionId });
    }

    [HttpPost("changeName")]
    public async Task<IActionResult> ChangeUsername([FromBody] UserDto userDto)
    {
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

        _logger.LogInformation("Username updated successfully for user {UserId}. NewUserName={UserName}", userId.Value, result.UserName);
        return Ok(result);
    }

    [HttpPost("changePassword")]
    public async Task<IActionResult> ChangePassword([FromQuery] string passwordHashOrKey, [FromQuery] int loginMethod = (int)LoginMethod.Password)
    {
        var userId = _sessionService.ProcessHeader(HttpContext.Request.Headers);
        if (!userId.HasValue)
        {
            _logger.LogWarning("Unauthorized username change attempt");
            return Unauthorized();
        }

        var user = await _repoManager.UserRepository.GetByIdAsync(userId.Value);
        if (user == null)
        {
            _logger.LogWarning("User not found for ID: {UserId}", userId.Value);
            return NotFound();
        }

        var parsedLoginMethod = (LoginMethod)loginMethod;
        if (user.LoginMethod != LoginMethod.Password || parsedLoginMethod != LoginMethod.Password)
        {
            _logger.LogWarning("Password change blocked for user {UserId} with login method {LoginMethod}.", userId.Value, user.LoginMethod);
            return BadRequest("Password can only be changed for password-based accounts.");
        }

        var passwordDto = new UserPaswordDto
        {
            PasswordHashOrKey = Uri.EscapeDataString(passwordHashOrKey),
            LoginMethod = parsedLoginMethod,
        };

        var result = await _repoManager.UserRepository.AddOrUpdateUserAsync(user, passwordDto);
        if (result == null)
        {
            _logger.LogError("Failed to update password for user ID: {UserId}", userId.Value);
            return BadRequest("Could not update password.");
        }

        _logger.LogInformation("Password updated successfully for user {UserId}.", userId.Value);
        return Ok();
    }

    private async Task<IActionResult> LoginWithGoogleIdTokenAsync(string idToken, string clientId)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId },
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google token validation failed.");
            return Unauthorized("Invalid Google token.");
        }

        return await CompleteGoogleLoginAsync(payload);
    }

    private async Task<IActionResult> CompleteGoogleLoginAsync(GoogleJsonWebSignature.Payload payload)
    {
        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject))
        {
            _logger.LogWarning("Google login rejected because email is not verified or mandatory claims are missing.");
            return Unauthorized("Google account email must be verified.");
        }

        var email = payload.Email.Trim();
        var googleKey = payload.Subject.Trim();
        var passwordDto = new UserPaswordDto
        {
            PasswordHashOrKey = googleKey,
            LoginMethod = LoginMethod.Google,
        };

        var existingUserByEmail = await _repoManager.UserRepository.GetByEmailAsync(email);
        UserDto? result;

        if (existingUserByEmail is null)
        {
            var baseUserName = BuildUserNameSeed(payload.Name, email);
            var uniqueUserName = await BuildUniqueUserNameAsync(baseUserName);
            result = await _repoManager.UserRepository.AddOrUpdateUserAsync(
                new UserDto
                {
                    Email = email,
                    UserName = uniqueUserName,
                    LoginMethod = LoginMethod.Google,
                },
                passwordDto);

            if (result == null)
            {
                _logger.LogError("Failed to create a user during Google login for email: {Email}", email);
                return BadRequest("Could not create user.");
            }
        }
        else
        {
            if (existingUserByEmail.LoginMethod != LoginMethod.Google)
            {
                _logger.LogWarning("Google login blocked because email {Email} belongs to a password-based account.", email);
                return Conflict("This email is already registered with email/password. Use password login.");
            }

            result = await _repoManager.UserRepository.TryLogin(existingUserByEmail, passwordDto);
            if (result == null)
            {
                // If Google key changed in DB, reconcile it for this verified Google identity.
                result = await _repoManager.UserRepository.AddOrUpdateUserAsync(existingUserByEmail, passwordDto);
                if (result == null)
                {
                    _logger.LogError("Failed to update Google key for existing user: {Email}", email);
                    return Unauthorized("Google login failed for this account.");
                }
            }
        }

        if (!result.Id.HasValue)
        {
            _logger.LogError("Google login succeeded but user ID is null for email: {Email}", email);
            return BadRequest("Can't retrieve user ID after login.");
        }

        var sessionId = _sessionService.CreateSession(result.Id.Value);
        await TryAddLoginEventAsync(result.Id.Value, LoginMethod.Google);
        _logger.LogInformation("Google login succeeded for user {Email}. UserId={UserId}, SessionId={SessionId}", email, result.Id.Value, sessionId);
        return Ok(new LoginResponse { User = result, SessionId = sessionId });
    }

    private async Task TryAddLoginEventAsync(Guid userId, LoginMethod provider)
    {
        try
        {
            await _repoManager.LoginEventRepository.AddLoginEvent(new LoginEventDto
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = provider,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to store login event for user {UserId}.", userId);
        }
    }

    private async Task<string> BuildUniqueUserNameAsync(string baseUserName)
    {
        var candidate = baseUserName;
        var suffix = 1;

        while (await _repoManager.UserRepository.GetByUserNameAsync(candidate) != null)
        {
            var suffixText = suffix.ToString(CultureInfo.InvariantCulture);
            var maxBaseLength = Math.Max(1, 32 - suffixText.Length);
            var basePart = baseUserName.Length > maxBaseLength ? baseUserName[..maxBaseLength] : baseUserName;
            candidate = $"{basePart}{suffixText}";
            suffix++;
        }

        return candidate;
    }

    private static string BuildUserNameSeed(string? googleName, string email)
    {
        var raw = string.IsNullOrWhiteSpace(googleName) ? email.Split('@')[0] : googleName;
        var sanitized = new string(raw
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "user";
        }

        if (sanitized.Length < 4)
        {
            sanitized = sanitized.PadRight(4, '0');
        }

        return sanitized.Length > 32 ? sanitized[..32] : sanitized;
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }
}



