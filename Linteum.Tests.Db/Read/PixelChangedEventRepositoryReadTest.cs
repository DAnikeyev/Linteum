using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Read;

internal class PixelChangedRepositoryReadTest : SyntheticDataTest
{
    [Test]
    public async Task GetPixelEvents()
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
        var pixelDto3 = new PixelDto
        {
            CanvasId = canvas!.Id,
            X = 0,
            Y = 1,
            ColorId = colors.First(x => x.Name == "Red").Id,
            Price = 4,
        };
        var pixel1 = await RepoManager.PixelRepository.TryChangePixelAsync(user!.Id!.Value, pixelDto1);
        var pixel2 = await RepoManager.PixelRepository.TryChangePixelAsync(user2!.Id!.Value, pixelDto2);
        var pixel3 = await RepoManager.PixelRepository.TryChangePixelAsync(user!.Id!.Value, pixelDto3);
        var pixelChangedEvents = (await pixelChangedRepo.GetByPixelIdAsync(pixel1.Id)).ToList();
        var canvaslChangedEvent = (await pixelChangedRepo.GetByCanvasIdAsync(pixel1.CanvasId, null)).ToList();
        var userChangedEvent = (await pixelChangedRepo.GetByUserIdAsync(user.Id.Value)).ToList();
        var userChangedEvent2 = (await pixelChangedRepo.GetByUserIdAsync(user2.Id.Value)).ToList();
        Assert.IsNotEmpty(pixelChangedEvents);
        Assert.IsNotEmpty(canvaslChangedEvent);
        Assert.IsNotEmpty(userChangedEvent);
        Assert.IsNotEmpty(userChangedEvent2);
        Assert.That(pixelChangedEvents.Count, Is.EqualTo(2));
        Assert.That(canvaslChangedEvent.Count, Is.EqualTo(3));
        Assert.That(userChangedEvent.Count, Is.EqualTo(2));
        Assert.That(userChangedEvent2.Count, Is.EqualTo(1));
    }
    
}