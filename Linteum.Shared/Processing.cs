using System.Security.Cryptography;
using System.Text;

namespace Linteum.Shared;

public static class Processing
{
    private static string Salt = Environment.GetEnvironmentVariable("PASSWORD_SALT") ?? String.Empty;
    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var saltedPassword = password + Salt;
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
        return Convert.ToBase64String(hashedBytes);
    }
}