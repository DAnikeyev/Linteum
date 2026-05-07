using Linteum.Shared.DTO;

namespace Linteum.Shared;

public static class GuestUserHelper
{
    public const string GuestUserNamePrefix = "guest";
    public const string GuestEmailDomain = "guestmail.com";

    public static bool IsGuest(UserDto? user) =>
        user != null && IsGuest(user.LoginMethod, user.Email);

    public static bool IsGuest(LoginMethod loginMethod, string? email = null) =>
        loginMethod == LoginMethod.Guest
        || (!string.IsNullOrWhiteSpace(email)
            && email.EndsWith($"@{GuestEmailDomain}", StringComparison.OrdinalIgnoreCase));

    public static string BuildGuestEmail(string userName) =>
        $"{userName}@{GuestEmailDomain}".ToLowerInvariant();
}
