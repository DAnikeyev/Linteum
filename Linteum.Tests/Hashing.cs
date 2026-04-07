using Linteum.Shared;

namespace Linteum.Tests;

public class Hashing
{
    private const string HashSalt = "salt";

    [Test]
    public void HashingTest()
    {
        var result = SecurityHelper.HashPassword("password", HashSalt);
        Console.WriteLine(result);
    }
}