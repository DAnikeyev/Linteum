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
            var passwordDto = new UserPasswordDto
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

        [Test]
        public async Task CreateGuestUser_SubscribesToMainAndSecondaryCanvases()
        {
            var guestUser = await RepoManager.UserRepository.CreateGuestUserAsync();

            Assert.That(guestUser, Is.Not.Null);
            Assert.That(guestUser!.Id, Is.Not.Null);
            Assert.That(guestUser.LoginMethod, Is.EqualTo(LoginMethod.Guest));

            var subscriptions = (await RepoManager.SubscriptionRepository.GetByUserIdAsync(guestUser.Id!.Value)).ToList();
            var subscribedCanvasIds = subscriptions.Select(subscription => subscription.CanvasId).ToHashSet();
            var autoSubscribedCanvasNames = new[] { DefaultConfig.DefaultCanvasName }
                .Concat(DefaultConfig.SecondaryDefaultCanvasNames);
            var expectedCanvases = (await RepoManager.CanvasRepository.GetAllAsync(includePrivates: true))
                .Where(canvas => autoSubscribedCanvasNames.Contains(canvas.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            Assert.That(expectedCanvases, Is.Not.Empty);

            foreach (var canvas in expectedCanvases)
            {
                Assert.That(subscribedCanvasIds.Contains(canvas.Id), Is.True, $"Guest user should be subscribed to '{canvas.Name}'.");
            }
        }
    }
}

