using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Update;

internal class PixelRepositoryUpdateTest : SyntheticDataTest
{
    [Test]
    public async Task TryUpdateAndReadPixel()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var user = await DbHelper.AddDefaultUser("User1");
        var user2 = await DbHelper.AddDefaultUser("User2");

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
        var subscription2 = await subscriptionRepo.Subscribe(user2!.Id!.Value, canvas.Id, password);
        Assert.IsNotNull(subscription);
        var colors = (await RepoManager.ColorRepository.GetAllAsync()).ToList();
        var colorBlack = colors.First(x => x.Name == "Black");
        var uid = user!.Id!.Value;
        var uid2 = user2!.Id!.Value;
        var paid = await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(uid, canvas.Id, 1000, BalanceChangedReason.Regular);
        var paid2 = await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(uid2, canvas.Id, 1000, BalanceChangedReason.Regular);

        var pixelDto = new PixelDto
        {
            CanvasId = canvas.Id,
            X = 5,
            Y = 5,
            ColorId = colorBlack.Id,
            Price = 2,
        };
        var newPixel = await pixelRepo.TryChangePixelAsync(uid, pixelDto);
        
        
        var pixelDto2 = new PixelDto
        {
            CanvasId = canvas.Id,
            X = 6,
            Y = 6,
            ColorId = colorBlack.Id,
            Price = 2,
        };
        var newPixel2 = await pixelRepo.TryChangePixelAsync(uid, pixelDto2);
        pixelDto.Price = 3;
        var newPixel2User2 = await pixelRepo.TryChangePixelAsync(uid2, pixelDto);
        Assert.IsNotNull(newPixel2User2);
        pixelDto.Price = 10;
        var newPixel2User1 = await pixelRepo.TryChangePixelAsync(uid, pixelDto);
        Assert.IsNotNull(newPixel2User1);
        var pixelsUser1 = (await pixelRepo.GetByOwnerIdAsync(uid)).ToList();
        Assert.That(pixelsUser1.Count, Is.EqualTo(2));
        
        var pixelsUser2 =(await pixelRepo.GetByOwnerIdAsync(user2!.Id!.Value)).ToList();
        Assert.That(pixelsUser2.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task NoMoneyToBuyPixel_ExpectNoChange()
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
        var subscription2 = await subscriptionRepo.Subscribe(user2!.Id!.Value, canvas.Id, password);
        Assert.IsNotNull(subscription);
        var colors = (await RepoManager.ColorRepository.GetAllAsync()).ToList();
        var colorBlack = colors.First(x => x.Name == "Black");
        var uid = user!.Id!.Value;
        var uid2 = user2!.Id!.Value;
        var paid = await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(uid, canvas.Id, 1000, BalanceChangedReason.Regular);
        var paid2 = await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(uid2, canvas.Id, 1000, BalanceChangedReason.Regular);

        var pixelDto = new PixelDto
        {
            CanvasId = canvas.Id,
            X = 5,
            Y = 5,
            ColorId = colorBlack.Id,
            Price = 500,
        };
        var newPixel = await pixelRepo.TryChangePixelAsync(uid, pixelDto);

        pixelDto.Price = 600;
        var newPixelUser2 = await pixelRepo.TryChangePixelAsync(uid2, pixelDto);
        
        pixelDto.Price = 700;
        var newPixel2User1 = await pixelRepo.TryChangePixelAsync(uid, pixelDto);
        Assert.IsNull(newPixel2User1);
        
        pixelDto.Price = 300;
        newPixel2User1 = await pixelRepo.TryChangePixelAsync(uid, pixelDto);
        Assert.IsNull(newPixel2User1);
        
        paid = await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(uid, canvas.Id, 1000, BalanceChangedReason.Regular);

        pixelDto.Price = 700;
        newPixel2User1 = await pixelRepo.TryChangePixelAsync(uid, pixelDto);
        Assert.IsNotNull(newPixel2User1);
    }
}