using System.Security.Cryptography;
using System.Text;
using NLog;

namespace Linteum.Shared;

public static class SecurityHelper
{
    private const bool LogPasswords = false;
    private static ILogger _logger = LogManager.GetCurrentClassLogger();
    private static string Salt = Environment.GetEnvironmentVariable("PASSWORD_SALT") ?? String.Empty;
    
    public static string HashPassword(string password, string? salt = null)
    {
        if(LogPasswords)
            _logger.Info("Hashing password: {Password} with salt: {Salt}", password, salt ?? Salt);
        using var sha256 = SHA256.Create();
        var saltedPassword = password + (salt ?? Salt);
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
        var result = Convert.ToBase64String(hashedBytes);
        if(LogPasswords)
            _logger.Info("Hashed password result: {Result}", result);
        return result;
    }
}