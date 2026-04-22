using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Read;

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
        var pixelChangedEvents = (await pixelChangedRepo.GetByPixelIdAsync(pixel1.Id!.Value)).ToList();
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

    [Test]
    public async Task CleanPixelHistoryBatchAsync_KeepsNewestEntries()
    {
        var user = await DbHelper.AddDefaultUser("BatchCleanupUser");
        var canvas = (await RepoManager.CanvasRepository.GetAllAsync()).FirstOrDefault();
        Assert.IsNotNull(canvas);

        var colors = (await RepoManager.ColorRepository.GetAllAsync()).ToList();
        var black = colors.First(x => x.Name == "Black");
        var red = colors.First(x => x.Name == "Red");
        PixelDto? latestPixel = null;

        for (var i = 0; i < 12; i++)
        {
            latestPixel = await RepoManager.PixelRepository.TryChangePixelAsync(user.Id!.Value, new PixelDto
            {
                CanvasId = canvas!.Id,
                X = 2,
                Y = 3,
                ColorId = i % 2 == 0 ? black.Id : red.Id,
            });
        }

        Assert.IsNotNull(latestPixel);
        Assert.IsNotNull(latestPixel!.Id);

        var deletedCount = await RepoManager.PixelChangedEventRepository.CleanPixelHistoryBatchAsync([latestPixel.Id.Value], 10);
        var pixelChangedEvents = (await RepoManager.PixelChangedEventRepository.GetByPixelIdAsync(latestPixel.Id.Value)).ToList();

        Assert.That(deletedCount, Is.EqualTo(2));
        Assert.That(pixelChangedEvents.Count, Is.EqualTo(10));
    }
    
}