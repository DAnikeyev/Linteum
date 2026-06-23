using System.Security.Cryptography;
using System.Text;

namespace Linteum.Shared;

/// <summary>
/// Password hashing/verification. Replaces the old plain SHA-256 scheme (P-SEC-04).
///
/// Credentials are hashed server-side with PBKDF2-HMAC-SHA256 (600k iterations),
/// a random per-credential salt, and a server-side pepper (the <c>PASSWORD_SALT</c>
/// env var, which used to be a global salt and is now only a pepper). The full output
/// (algorithm + iteration count + salt + hash) is encoded as a self-describing string
/// so it fits in the existing <c>PasswordHashOrKey</c> columns without a DB migration.
///
/// <see cref="VerifyPassword"/> auto-detects legacy stored values (old SHA-256-base64
/// hashes, or plaintext bot credentials) and verifies them in constant time, flagging
/// them for a lazy upgrade to the new scheme on the next successful login.
/// </summary>
public static class SecurityHelper
{
    /// <summary>Prefix identifying the current scheme, e.g. <c>LTS1$PBKDF2-SHA256$...</c>.</summary>
    public const string SchemePrefix = "LTS1$";

    private const string Algorithm = "PBKDF2-SHA256";
    private const int DefaultIterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>
    /// Server-side pepper. The former global <c>PASSWORD_SALT</c> is now used only as a
    /// pepper (appended to the password before the KDF). Falls back to empty for parity
    /// with legacy hashes that were created without a salt configured.
    /// </summary>
    private static string Pepper => Environment.GetEnvironmentVariable("PASSWORD_SALT") ?? string.Empty;

    /// <summary>
    /// Hashes a password with a fresh random salt, returning a self-describing string.
    /// </summary>
    public static string HashPassword(string password, string? pepper = null)
    {
        password ??= string.Empty;
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = ComputePbkdf2(password, pepper ?? Pepper, salt, DefaultIterations);
        return $"{SchemePrefix}{Algorithm}${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a plaintext password against a stored value, auto-detecting the format.
    /// Returns <c>(valid, needsRehash)</c>; <c>needsRehash</c> is true when the stored
    /// value is a legacy format that should be upgraded via <see cref="HashPassword"/>.
    /// </summary>
    public static (bool valid, bool needsRehash) VerifyPassword(string? password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
        {
            return (false, false);
        }

        if (stored!.StartsWith(SchemePrefix, StringComparison.Ordinal))
        {
            return (VerifyPbkdf2(password, Pepper, stored), false);
        }

        // Legacy: either a plaintext credential (bots) or an old SHA-256(password+pepper) hash.
        if (FixedTimeEqualsString(password, stored))
        {
            return (true, true);
        }

        if (FixedTimeEqualsString(LegacySha256(password), stored))
        {
            return (true, true);
        }

        return (false, false);
    }

    private static bool VerifyPbkdf2(string password, string pepper, string stored)
    {
        var parts = stored.Substring(SchemePrefix.Length).Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        var salt = TryFromBase64(parts[2]);
        var expected = TryFromBase64(parts[3]);
        if (salt is null || expected is null)
        {
            return false;
        }

        var actual = ComputePbkdf2(password, pepper, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] ComputePbkdf2(string password, string pepper, byte[] salt, int iterations)
    {
        var input = Encoding.UTF8.GetBytes(password + pepper);
        using var kdf = new Rfc2898DeriveBytes(input, salt, iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(HashBytes);
    }

    /// <summary>
    /// The old scheme, kept only to verify/migrate legacy credentials:
    /// Base64(SHA-256(password + pepper)). Must stay byte-for-byte compatible with the
    /// hashes produced by the previous client-side implementation.
    /// </summary>
    public static string LegacySha256(string password, string? pepper = null)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + (pepper ?? Pepper)));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Constant-time string comparison; false (not throw) on length mismatch.</summary>
    public static bool FixedTimeEqualsString(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static byte[]? TryFromBase64(string s)
    {
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
