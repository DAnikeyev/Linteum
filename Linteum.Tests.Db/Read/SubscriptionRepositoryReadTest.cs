using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Delete
{
    internal class SubscriptionRepositoryReadTest : SyntheticDataTest
    {
        [Test]
        public async Task GetSubscription()
        {
            var userRepo = RepoManager.UserRepository;
            var user1 = await DbHelper.AddDefaultUser("user1");
            var user2 = await DbHelper.AddDefaultUser("user2");
            var user3 = await DbHelper.AddDefaultUser("user3");
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
            var newSubscription1 = await subscriptionRepo.Subscribe(user1!.Id!.Value, canvas.Id, password);
            var newSubscription2 = await subscriptionRepo.Subscribe(user2!.Id!.Value, canvas.Id, password);
            var canvasSub = (await subscriptionRepo.GetByCanvasIdAsync(canvas.Id)).ToList();
            var defaultCanvas = await RepoManager.CanvasRepository.GetByNameAsync(DefaultConfig.DefaultCanvasName);
            var homeSub = (await subscriptionRepo.GetByCanvasIdAsync(defaultCanvas!.Id)).ToList();
            Assert.IsTrue(canvasSub.Any(x=> x.UserId == user1!.Id!.Value));
            Assert.IsTrue(canvasSub.Any(x=> x.UserId == user2!.Id!.Value));
            Assert.IsTrue(!canvasSub.Any(x=> x.UserId == user3!.Id!.Value));
            
            Assert.IsTrue(homeSub.Any(x=> x.UserId == user1!.Id!.Value));
            Assert.IsTrue(homeSub.Any(x=> x.UserId == user2!.Id!.Value));
            Assert.IsTrue(homeSub.Any(x=> x.UserId == user3!.Id!.Value));
            
        }
    }
}

