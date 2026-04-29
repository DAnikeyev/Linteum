using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Delete;

internal class CanvasRepositoryDeleteTest : SyntheticDataTest
{
    [Test]
    public async Task TryEraseCanvas_ClearsPixelsAndHistory()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var pixelHistoryRepo = RepoManager.PixelChangedEventRepository;
        var creator = await DbHelper.AddDefaultUser("canvas-erase-owner");
        Assert.That(creator?.Id, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var canvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = creator!.Id!.Value,
            Name = "Erase Test Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.FreeDraw,
        }, null);

        Assert.That(canvas, Is.Not.Null);

        await pixelRepo.TryChangePixelAsync(creator.Id.Value, new PixelDto { CanvasId = canvas!.Id, X = 1, Y = 1, ColorId = black.Id });
        await pixelRepo.TryChangePixelAsync(creator.Id.Value, new PixelDto { CanvasId = canvas.Id, X = 2, Y = 2, ColorId = black.Id });

        Assert.That((await pixelRepo.GetByCanvasIdAsync(canvas.Id)).Count(), Is.EqualTo(2));
        Assert.That((await pixelHistoryRepo.GetByCanvasIdAsync(canvas.Id, null)).Count(), Is.EqualTo(2));

        var eraseResult = await canvasRepo.TryEraseCanvasByName(canvas.Name);

        Assert.That(eraseResult, Is.True);
        Assert.That(await canvasRepo.GetByNameAsync(canvas.Name), Is.Not.Null);
        Assert.That((await pixelRepo.GetByCanvasIdAsync(canvas.Id)).Count(), Is.EqualTo(0));
        Assert.That((await pixelHistoryRepo.GetByCanvasIdAsync(canvas.Id, null)).Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task TryDeleteCanvas_RemovesCanvasAndRelatedData()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var pixelHistoryRepo = RepoManager.PixelChangedEventRepository;
        var subscriptionRepo = RepoManager.SubscriptionRepository;
        var balanceRepo = RepoManager.BalanceChangedEventRepository;
        var creator = await DbHelper.AddDefaultUser("canvas-delete-owner");
        var subscriber = await DbHelper.AddDefaultUser("canvas-delete-subscriber");
        Assert.That(creator?.Id, Is.Not.Null);
        Assert.That(subscriber?.Id, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var canvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = creator!.Id!.Value,
            Name = "Delete Test Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.FreeDraw,
        }, null);

        Assert.That(canvas, Is.Not.Null);

        await subscriptionRepo.Subscribe(creator.Id.Value, canvas!.Id, null);
        await subscriptionRepo.Subscribe(subscriber!.Id!.Value, canvas.Id, null);
        await balanceRepo.TryChangeBalanceAsync(creator.Id.Value, canvas.Id, 5, BalanceChangedReason.Regular);
        await balanceRepo.TryChangeBalanceAsync(subscriber.Id.Value, canvas.Id, 7, BalanceChangedReason.Regular);
        await pixelRepo.TryChangePixelAsync(creator.Id.Value, new PixelDto { CanvasId = canvas.Id, X = 3, Y = 3, ColorId = black.Id });

        Assert.That((await subscriptionRepo.GetByCanvasIdAsync(canvas.Id)).Count(), Is.EqualTo(2));
        Assert.That((await balanceRepo.GetByUserAndCanvasIdAsync(creator.Id.Value, canvas.Id)).Any(), Is.True);
        Assert.That((await pixelRepo.GetByCanvasIdAsync(canvas.Id)).Any(), Is.True);
        Assert.That((await pixelHistoryRepo.GetByCanvasIdAsync(canvas.Id, null)).Any(), Is.True);

        var deleteProtected = await canvasRepo.TryDeleteCanvasByName(DefaultConfig.DefaultCanvasName);
        var deleteResult = await canvasRepo.TryDeleteCanvasByName(canvas.Name);

        Assert.That(deleteProtected, Is.False);
        Assert.That(deleteResult, Is.True);
        Assert.That(await canvasRepo.GetByNameAsync(canvas.Name), Is.Null);
        Assert.That((await subscriptionRepo.GetByCanvasIdAsync(canvas.Id)).Count(), Is.EqualTo(0));
        Assert.That((await balanceRepo.GetByUserAndCanvasIdAsync(creator.Id.Value, canvas.Id)).Count(), Is.EqualTo(0));
        Assert.That((await balanceRepo.GetByUserAndCanvasIdAsync(subscriber.Id.Value, canvas.Id)).Count(), Is.EqualTo(0));
        Assert.That((await pixelRepo.GetByCanvasIdAsync(canvas.Id)).Count(), Is.EqualTo(0));
        Assert.That((await pixelHistoryRepo.GetByCanvasIdAsync(canvas.Id, null)).Count(), Is.EqualTo(0));
    }
}