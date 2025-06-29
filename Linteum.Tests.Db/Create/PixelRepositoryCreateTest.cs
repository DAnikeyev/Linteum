using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Create;

internal class PixelRepositoryCreateTest : SyntheticDataTest
{
    [Test]
    public async Task TryAddPixel()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var user = await DbHelper.AddDefaultUser("User1");
        var user2 = await DbHelper.AddDefaultUser("User2");
        // Create a canvas
        var canvasDto = new CanvasDto
        {
            Name = "Test Canvas",
            Width = 10,
            Height = 10,
        };
        var password = "testpassword";
        var canvas = await canvasRepo.TryAddCanvas(canvasDto, password);
        Assert.IsNotNull(canvas);
        var subscriptionRepo = RepoManager.SubscriptionRepository;
        var subscription = await subscriptionRepo.Subscribe(user!.Id!.Value, canvas.Id, password);
        Assert.IsNotNull(subscription);
        var paid = await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(user.Id.Value, canvas.Id, 1000, BalanceChangedReason.Regular);
        var colors = (await RepoManager.ColorRepository.GetAllAsync()).ToList();
        var colorBlack = colors.First(x => x.Name == "Black");
        var pixelDto = new PixelDto
        {
            CanvasId = canvas.Id,
            X = 5,
            Y = 5,
            ColorId = colorBlack.Id,
            Price = 2,
        };
        var newPixel = await pixelRepo.TryChangePixelAsync(user.Id.Value, pixelDto);
        
        Assert.IsNotNull(newPixel);
        Assert.That(newPixel.CanvasId, Is.EqualTo(canvas.Id));
        Assert.That(newPixel.X, Is.EqualTo(pixelDto.X));
        Assert.That(newPixel.Y, Is.EqualTo(pixelDto.Y));
    }
}