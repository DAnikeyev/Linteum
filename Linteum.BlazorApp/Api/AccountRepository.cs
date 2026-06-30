using System.Net;
using Linteum.BlazorApp.ExtensionMethods;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>
/// Account access: login (password / session-id / Google code / guest), signup, change-username,
/// change-password. Extracted from <c>MyApiClient</c> (P‑MAIN‑03). Successful auth persists the
/// session via <see cref="SessionStore"/>.
/// </summary>
internal sealed class AccountRepository
{
    private readonly ApiHttp _http;
    private readonly SessionStore _session;
    private readonly ILogger<AccountRepository> _logger;

    public AccountRepository(ApiHttp http, SessionStore session, ILogger<AccountRepository> logger)
    {
        _http = http;
        _session = session;
        _logger = logger;
    }

    public async Task<(UserDto? User, Guid? SessionId)> LoginAsync(string email, string password)
    {
        _logger.LogInformation("LoginAsync called with email: {Email}", email);
        var response = await _http.Client.PostAsJsonAsync("/users/login", new LoginRequestDto { Email = email, Password = password });
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Login failed for email: {Email}, status: {StatusCode}", email, response.StatusCode);
            return (null, null);
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            await _session.PersistAuthenticatedUserAsync(loginResponse.User, loginResponse.SessionId.Value);
            _logger.LogInformation("Login successful for email: {Email}", email);
        }
        else
        {
            _logger.LogWarning("Login response data missing for email: {Email}", email);
        }

        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<(UserDto? User, Guid? SessionId, string? Error)> LoginWithGoogleCodeAsync(string code)
    {
        _logger.LogInformation("LoginWithGoogleCodeAsync called.");
        var response = await _http.Client.PostAsJsonAsync(
            "/users/login-google-code",
            new GoogleLoginCodeRequestDto { Code = code });

        if (!response.IsSuccessStatusCode)
        {
            var error = ApiErrors.ParseErrorMessage(await response.Content.ReadAsStringAsync(), "Google login failed.");
            _logger.LogWarning("Google login failed, status: {StatusCode}, error: {Error}", response.StatusCode, error);
            return (null, null, error);
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            var googleUser = loginResponse.User;
            await _session.PersistAuthenticatedUserAsync(googleUser, loginResponse.SessionId.Value);
            _logger.LogInformation("Google login successful for email: {Email}", googleUser.Email);
            return (googleUser, loginResponse.SessionId, null);
        }

        _logger.LogWarning("Google login response data missing.");
        return (null, null, "Google login failed.");
    }

    public async Task<(UserDto? User, Guid? SessionId)> LoginAsync(Guid sessionId)
    {
        _logger.LogInformation("LoginAsync called with sessionId: {SessionId}", sessionId);

        var response = await _http.Client.PostAsJsonAsync("/users/validate", sessionId);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Session validation failed for sessionId: {SessionId}, status: {StatusCode}", sessionId, response.StatusCode);
            return (null, null);
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            await _session.PersistAuthenticatedUserAsync(loginResponse.User, loginResponse.SessionId.Value);
            _logger.LogInformation("Session validation successful for sessionId: {SessionId}", sessionId);
        }
        else
        {
            _logger.LogWarning("Session validation response data missing for sessionId: {SessionId}", sessionId);
        }

        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<(UserDto? User, Guid? SessionId)> LoginAsGuestAsync()
    {
        _logger.LogInformation("LoginAsGuestAsync called.");
        var response = await _http.Client.PostAsync("/users/login-guest", content: null);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Guest login failed, status: {StatusCode}", response.StatusCode);
            return (null, null);
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            await _session.PersistAuthenticatedUserAsync(loginResponse.User, loginResponse.SessionId.Value);
            _logger.LogInformation("Guest login successful for email: {Email}", loginResponse.User.Email);
        }
        else
        {
            _logger.LogWarning("Guest login response data missing.");
        }

        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task<(UserDto? User, Guid? SessionId)> SignupAsync(string email, string password, string userName)
    {
        _logger.LogInformation("SignupAsync called with email: {Email}, userName: {UserName}", email, userName);
        var response = await _http.Client.PostAsJsonAsync("/users/add", new SignupRequestDto { Email = email, UserName = userName, Password = password });
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Signup failed for email: {Email}, status: {StatusCode}", email, response.StatusCode);
            return (null, null);
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse?.SessionId != null && loginResponse.User != null && loginResponse.User.UserName != null && loginResponse.User.Email != null)
        {
            await _session.PersistAuthenticatedUserAsync(loginResponse.User, loginResponse.SessionId.Value);
            _logger.LogInformation("Signup successful for email: {Email}", email);
        }
        else
        {
            _logger.LogWarning("Signup response data missing for email: {Email}", email);
        }

        return (loginResponse?.User, loginResponse?.SessionId);
    }

    public async Task ChangeUsernameAsync(string userName)
    {
        _logger.LogInformation("ChangeUsernameAsync called with userName: {UserName}", userName);
        if (string.IsNullOrEmpty(userName))
            throw new ArgumentException("Username cannot be empty", nameof(userName));
        var existingEmail = await _http.Storage.GetItemAsync<string>(LocalStorageKey.Email);
        if (string.IsNullOrEmpty(existingEmail))
            throw new InvalidOperationException("Email is not set in local storage.");
        var existingLoginMethod = await _http.Storage.GetItemAsync<LoginMethod>(LocalStorageKey.LoginMethod);
        var userDto = new UserDto { UserName = userName, Email = existingEmail, LoginMethod = existingLoginMethod };
        var request = await _http.CreateAsync(HttpMethod.Post, "/users/changeName");
        request.SetJsonContent(userDto);
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully changed username to {UserName}", userName);
            await _http.Storage.SetItemAsync(LocalStorageKey.UserName, userName);
        }
        else
        {
            _logger.LogWarning("Failed to change username to {UserName}, Status: {StatusCode}", userName, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                throw new Exception("Service is currently unavailable. Please try again later.");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("You are not authorized to change the username. Please log in again.");
            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new Exception("Failed to change username. User already exists.");
        }
    }

    public async Task ChangePasswordAsync(string password)
    {
        _logger.LogInformation("ChangePasswordAsync called");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var request = await _http.CreateAsync(HttpMethod.Post, "/users/changePassword");
        request.SetJsonContent(new ChangePasswordRequestDto { NewPassword = password });
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully changed password");
        }
        else
        {
            _logger.LogWarning("Failed to change password, Status: {StatusCode}", response.StatusCode);
            throw new Exception("Failed to change password.");
        }
    }
}
