using Linteum.Shared.DTO;
using Linteum.Shared.Exceptions;

namespace Linteum.Tests.Db.Delete
{
    internal class SubscriptionRepositoryDeleteTest : SyntheticDataTest
    {
        [Test]
        public async Task TryDeleteSubscription()
        {
            var userRepo = RepoManager.UserRepository;
            var user = await DbHelper.AddDefaultUser();
            var canvasDto = new CanvasDto
            {
                CreatorId = user!.Id!.Value,
                Name = "Test Canvas",
                Width = 10,
                Height = 10,
            };
            var password = "testpassword";
            var canvas = await RepoManager.CanvasRepository.TryAddCanvas(canvasDto, password);
            Assert.IsNotNull(canvas);
            var subscriptionRepo = RepoManager.SubscriptionRepository;
            Assert.ThrowsAsync<InvalidCanvasPasswordException>(async () =>
                await subscriptionRepo.Subscribe(user!.Id!.Value, canvas.Id, "wrongPassword"));
            var newSubscription = await subscriptionRepo.Subscribe(user.Id.Value, canvas.Id, password);
            Assert.IsNotNull(newSubscription);
            Assert.That(newSubscription.UserId, Is.EqualTo(user.Id));
            Assert.That(newSubscription.CanvasId, Is.EqualTo(canvas.Id));
            
            Assert.ThrowsAsync<CanvasNotFoundException>(async () =>
                await subscriptionRepo.Unsubscribe(user.Id.Value, Guid.Empty));
            var unsub = await subscriptionRepo.Unsubscribe(user.Id.Value, canvas.Id);
            Assert.IsNotNull(unsub);
        }
    }
}

