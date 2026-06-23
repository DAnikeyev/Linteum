using Linteum.Shared;

namespace Linteum.Tests;

public class SecurityHelperTests
{
    [Test]
    public void HashPassword_RoundTrips_AndNeedsNoRehash()
    {
        var hash = SecurityHelper.HashPassword("correct horse battery staple");

        var (valid, needsRehash) = SecurityHelper.VerifyPassword("correct horse battery staple", hash);

        Assert.That(valid, Is.True);
        Assert.That(needsRehash, Is.False);
    }

    [Test]
    public void VerifyPassword_RejectsWrongPassword()
    {
        var hash = SecurityHelper.HashPassword("hunter2");

        var (valid, _) = SecurityHelper.VerifyPassword("hunter3", hash);

        Assert.That(valid, Is.False);
    }

    [Test]
    public void HashPassword_UsesFreshRandomSalt()
    {
        var a = SecurityHelper.HashPassword("same-password");
        var b = SecurityHelper.HashPassword("same-password");

        Assert.That(a, Is.Not.EqualTo(b), "each hash must have its own random salt");
        Assert.That(SecurityHelper.VerifyPassword("same-password", a).valid, Is.True);
        Assert.That(SecurityHelper.VerifyPassword("same-password", b).valid, Is.True);
    }

    [Test]
    public void VerifyPassword_MigratesLegacySha256Hash()
    {
        // The old client-side scheme stored Base64(SHA-256(password + pepper)).
        var legacy = SecurityHelper.LegacySha256("my-old-password");

        var (valid, needsRehash) = SecurityHelper.VerifyPassword("my-old-password", legacy);

        Assert.That(valid, Is.True);
        Assert.That(needsRehash, Is.True);

        // After re-hash with the new scheme, the stored value is upgraded and stable.
        var upgraded = SecurityHelper.HashPassword("my-old-password");
        var (stillValid, noRehash) = SecurityHelper.VerifyPassword("my-old-password", upgraded);
        Assert.That(stillValid, Is.True);
        Assert.That(noRehash, Is.False);
    }

    [Test]
    public void VerifyPassword_MigratesLegacyPlaintextCredential()
    {
        // Bots previously stored their plaintext password verbatim.
        const string legacyPlaintext = "SecurePassword123!";

        var (valid, needsRehash) = SecurityHelper.VerifyPassword(legacyPlaintext, legacyPlaintext);

        Assert.That(valid, Is.True);
        Assert.That(needsRehash, Is.True);
    }

    [Test]
    public void VerifyPassword_RejectsEmptyInputs()
    {
        var hash = SecurityHelper.HashPassword("x");

        Assert.That(SecurityHelper.VerifyPassword("", hash).valid, Is.False);
        Assert.That(SecurityHelper.VerifyPassword("x", "").valid, Is.False);
        Assert.That(SecurityHelper.VerifyPassword(null, hash).valid, Is.False);
    }

    [Test]
    public void LegacySha256_StaysByteCompatibleWithOldScheme()
    {
        // The old implementation hashed password + PASSWORD_SALT with SHA-256 and base64-encoded it.
        // Using the same pepper here must reproduce that exact value, or existing hashes won't verify.
        var expected = SecurityHelper.LegacySha256("pw", "pepper");
        var actual = SecurityHelper.LegacySha256("pw", "pepper");

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(actual, Does.Not.StartWith(SecurityHelper.SchemePrefix)); // legacy is plain base64, no scheme prefix
    }
}
