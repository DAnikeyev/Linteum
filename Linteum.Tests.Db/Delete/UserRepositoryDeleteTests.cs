using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Delete;

internal class UserRepositoryDeleteTests : SyntheticDataTest
{
    [Test]
    public async Task TryDeleteExistingUser()
    {
        var userRepo = RepoManager.UserRepository;
        var user = new UserDto
        {
            Email = "123@gmail.com",
            UserName = "TestUser",
            LoginMethod = LoginMethod.Password,
        };
        var passwordDto = new PasswordDto
        {
            PasswordHashOrKey = "hash",
            LoginMethod = LoginMethod.Password,
        };
        var newUser = await userRepo.AddOrUpdateUserAsync(user, passwordDto);

        var user1 = await userRepo.GetByEmailAsync("123@gmail.com");
        var user2 = await userRepo.GetByUserNameAsync("TestUser");
        var user3 = await userRepo.GetByIdAsync(newUser!.Id!.Value);

        Assert.IsNotNull(user1);
        Assert.IsNotNull(user2);
        Assert.IsNotNull(user3);
        Assert.That(user1.Id, Is.EqualTo(newUser.Id));
        Assert.That(user2.Id, Is.EqualTo(newUser.Id));
        Assert.That(user3.Id, Is.EqualTo(newUser.Id));
        Assert.That(user1.Email, Is.EqualTo("123@gmail.com"));
        Assert.That(user2.UserName, Is.EqualTo("TestUser"));

        var result = await userRepo.DeleteUserAsync(newUser);
        Assert.IsNotNull(result);
        Assert.IsNull(await userRepo.GetByEmailAsync("123@gmail.com"));
        Assert.IsNull(await userRepo.GetByUserNameAsync("TestUser"));
        Assert.IsNull(await userRepo.GetByIdAsync(newUser!.Id!.Value));
    }

    [Test]
    public async Task TryDeleteNonExistingUser()
    {
        var userRepo = RepoManager.UserRepository;
        var nonExistinguUser = new UserDto
        {
            Email = "123@gmail.com",
            UserName = "TestUser",
            LoginMethod = LoginMethod.Password,
        };
        var deleted = await userRepo.DeleteUserAsync(nonExistinguUser);
        Assert.That(deleted, Is.Null);
    }
}