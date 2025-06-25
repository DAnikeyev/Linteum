using Linteum.Shared.DTO;

namespace Linteum.Tests.Create
{
    internal class SubscriptionRepositoryCreateTest : SyntheticDataTest
    {
        [Test]
        public async Task TryAddSubscription()
        {
            var userRepo = RepoManager.UserRepository;
            var user = await DbHelper.AddDefaultUser();
            var canvasDto = new CanvasDto
            {
                Name = "Test Canvas",
                Width = 10,
                Height = 10,
            };
            var password = "testpassword";
            var canvas = await RepoManager.CanvasRepository.TryAddCanvas(canvasDto, password);
            Assert.IsNotNull(canvas);
            var subscriptionRepo = RepoManager.SubscriptionRepository;
            var newSubscriptionWrong = await subscriptionRepo.Subscribe(user!.Id!.Value, canvas.Id, "wrongPassword");
            Assert.IsNull(newSubscriptionWrong);
            var newSubscription = await subscriptionRepo.Subscribe(user.Id.Value, canvas.Id, password);
            Assert.IsNotNull(newSubscription);
            Assert.That(newSubscription.UserId, Is.EqualTo(user.Id));
            Assert.That(newSubscription.CanvasId, Is.EqualTo(canvas.Id));
        }
    }
}

