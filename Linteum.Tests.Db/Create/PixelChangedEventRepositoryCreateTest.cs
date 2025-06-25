using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Create;

internal class PixelChangedEventRepositoryCreateTest : SyntheticDataTest
{
    [Test]
    public async Task AddPixelEvents()
    {
        var pixelChangedRepo = RepoManager.PixelChangedEventRepository;
        var user = await DbHelper.AddDefaultUser("U1");
        var user2 = await DbHelper.AddDefaultUser("U2");
        var canvas = (await RepoManager.CanvasRepository.GetAllAsync()).FirstOrDefault();
        await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(user.Id.Value, canvas.Id, 1000, BalanceChangedReason.Regular);
        await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(user2.Id.Value, canvas.Id, 1000, BalanceChangedReason.Regular);
        var colors = (await RepoManager.ColorRepository.GetAllAsync()).ToList();
        var pixelDto1 = new PixelDto
        {
            CanvasId = canvas!.Id,
            X = 0,
            Y = 0,
            ColorId = colors.First(x => x.Name == "Black").Id,
            Price = 2,
        };
        var pixelDto2 = new PixelDto
        {
            CanvasId = canvas!.Id,
            X = 0,
            Y = 0,
            ColorId = colors.First(x => x.Name == "Red").Id,
            Price = 4,
        };
        var pixel1 = await RepoManager.PixelRepository.TryChangePixelAsync(user!.Id!.Value, pixelDto1);
        var pixel2 = await RepoManager.PixelRepository.TryChangePixelAsync(user2!.Id!.Value, pixelDto2);
        Assert.IsNotNull(pixel1);
        Assert.IsNotNull(pixel2);
        Assert.That(pixel2.CanvasId, Is.EqualTo(pixel1.CanvasId));
        Assert.That(pixel2.Id, Is.EqualTo(pixel1.Id));
        var pixelChangedEvent = (await pixelChangedRepo.GetByPixelIdAsync(pixel1.Id)).ToList();
        Assert.That(pixelChangedEvent.Count, Is.EqualTo(2));
        Assert.That(pixelChangedEvent[0].OwnerUserId, Is.EqualTo(user.Id));
        Assert.That(pixelChangedEvent[0].OldOwnerUserId, Is.Null);
        Assert.That(pixelChangedEvent[0].NewColorId, Is.EqualTo(pixelDto1.ColorId));
        Assert.That(pixelChangedEvent[0].NewPrice, Is.EqualTo(pixelDto1.Price));
        
        Assert.That(pixelChangedEvent[1].OwnerUserId, Is.EqualTo(user2.Id));
        Assert.That(pixelChangedEvent[1].OldOwnerUserId, Is.EqualTo(user.Id));
        Assert.That(pixelChangedEvent[1].NewColorId, Is.EqualTo(pixelDto2.ColorId));
        Assert.That(pixelChangedEvent[1].OldColorId, Is.EqualTo(pixelDto1.ColorId));
        Assert.That(pixelChangedEvent[1].NewPrice, Is.EqualTo(pixelDto2.Price));
    }
    
}