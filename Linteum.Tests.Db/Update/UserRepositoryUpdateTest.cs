using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Update;

internal class UserRepositoryUpdateTest : SyntheticDataTest
{
    [Test]
    public async Task TryUpdateUser()
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
        Assert.IsNotNull(newUser);

        newUser.Email = "456@gmail.com";
        newUser.LoginMethod = LoginMethod.Google;
        newUser.UserName = "NewCoolUserName";

        var newPasswordDto = new PasswordDto
        {
            PasswordHashOrKey = "hash2",
            LoginMethod = LoginMethod.Google,
        };
        
        var updatedUser = await userRepo.AddOrUpdateUserAsync(newUser, newPasswordDto);
        Assert.IsNotNull(updatedUser);
        Assert.That(updatedUser.Email, Is.EqualTo(newUser.Email));
        Assert.That(updatedUser.UserName, Is.EqualTo(newUser.UserName));
        Assert.That(updatedUser.LoginMethod, Is.EqualTo(newUser.LoginMethod));
        var oldUser = await userRepo.GetByEmailAsync(user.Email);
        Assert.IsNull(oldUser);
        
        var login1 = await userRepo.TryLogin(updatedUser, newPasswordDto);
        Assert.IsNotNull(login1);
        var login2 = await userRepo.TryLogin(updatedUser, passwordDto);
        Assert.IsNull(login2);
    }
}