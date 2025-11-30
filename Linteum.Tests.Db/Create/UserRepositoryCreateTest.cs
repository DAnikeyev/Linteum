using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Create
{
    internal class UserRepositoryCreateTest : SyntheticDataTest
    {
        [Test]
        public async Task TryAddUser()
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
            Assert.IsNotNull(newUser);
            Assert.That(newUser.Email, Is.EqualTo(user.Email));
            Assert.That(newUser.UserName, Is.EqualTo(user.UserName));
            Assert.That(newUser.LoginMethod, Is.EqualTo(user.LoginMethod));
            Assert.IsNotNull(newUser.Id);
        }
    }
}

