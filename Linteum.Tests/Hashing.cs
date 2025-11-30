using Linteum.Shared;

namespace Linteum.Tests;

public class Hashing
{
    private const string HashSalt = "salt";

    [Test]
    public void HashingTest()
    {
        Console.WriteLine(SecurityHelper.HashPassword("password", HashSalt));
    }
}