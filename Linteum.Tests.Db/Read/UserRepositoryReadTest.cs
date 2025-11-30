using System;
using System.Linq;
using System.Threading.Tasks;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using NUnit.Framework;

namespace Linteum.Tests.Db.Read;

internal class UserRepositoryReadTest : SyntheticDataTest
{

    [Test]
    public async Task GetExistingUser()
    {
        var userRepo = RepoManager.UserRepository;
        var user = new UserDto
        {
            Email = "123@gmail.com",
            UserName = "TestUser",
            LoginMethod = LoginMethod.Password,
        };
        var passwordDto = new UserPaswordDto
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
    }

    [Test]
    public async Task TryLogin()
    {
        var userRepo = RepoManager.UserRepository;
        var user = new UserDto
        {
            Email = "123@gmail.com",
            UserName = "TestUser",
            LoginMethod = LoginMethod.Password,
        };
        var passwordDto = new UserPaswordDto
        {
            PasswordHashOrKey = "hash",
            LoginMethod = LoginMethod.Password,
        };
        var newUser = await userRepo.AddOrUpdateUserAsync(user, passwordDto);
        var loginUser = new UserDto
        {
            Email = "123@gmail.com",
            UserName = "TestUser",
            Id = newUser.Id,
            LoginMethod = LoginMethod.Password,
        };
        var loginPasswordDto = new UserPaswordDto
        {
            PasswordHashOrKey = "hash",
            LoginMethod = LoginMethod.Password,
        };
        var loggedInUser = await userRepo.TryLogin(loginUser, loginPasswordDto);
        Assert.IsNotNull(loggedInUser);
        Assert.That(loggedInUser.Id, Is.EqualTo(newUser.Id));
    }

    [Test]
    public async Task GetNonExistingUser()
    {
        var userRepo = RepoManager.UserRepository;
        var nonExistingId = Guid.NewGuid();

        var userById = await userRepo.GetByIdAsync(nonExistingId);
        var userByEmail = await userRepo.GetByEmailAsync("nonexistent@example.com");
        var userByUserName = await userRepo.GetByUserNameAsync("NonExistentUser");

        Assert.IsNull(userById);
        Assert.IsNull(userByEmail);
        Assert.IsNull(userByUserName);
    }
    
    [Test]
    public async Task TryLoginWithWrongPassword()
    {
        var userRepo = RepoManager.UserRepository;
        var user = new UserDto
        {
            Email = "123@gmail.com",
            UserName = "TestUser",
            LoginMethod = LoginMethod.Password,
        };
        var passwordDto = new UserPaswordDto
        {
            PasswordHashOrKey = "hash",
            LoginMethod = LoginMethod.Password,
        };
        var newUser = await userRepo.AddOrUpdateUserAsync(user, passwordDto);
        var loginUser = new UserDto
        {
            Email = "123@gmail.com",
            UserName = "TestUser",
            Id = newUser.Id,
            LoginMethod = LoginMethod.Password,
        };
        var loginPasswordDto = new UserPaswordDto
        {
            PasswordHashOrKey = "wronghash",
            LoginMethod = LoginMethod.Password,
        };
        var loggedInUser = await userRepo.TryLogin(loginUser, loginPasswordDto);
        Assert.IsNull(loggedInUser);
    }
}