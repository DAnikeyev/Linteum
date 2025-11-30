using System.Security.Cryptography;
using System.Text;

namespace Linteum.Shared;

public static class SecurityHelper
{
    private static string Salt = Environment.GetEnvironmentVariable("PASSWORD_SALT") ?? String.Empty;
    public static string HashPassword(string password, string? salt = null)
    {
        using var sha256 = SHA256.Create();
        var saltedPassword = password + (salt ?? Salt);
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
        return Convert.ToBase64String(hashedBytes);
    }
}