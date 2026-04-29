using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.Extensions.Logging;

namespace Linteum.Tests.Db.Update;

internal class HourlyCanvasIncomeProcessorTest : SyntheticDataTest
{
    [Test]
    public async Task ProcessAsync_PaysSubscribedEconomyUsersByOwnedPixels()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var subscriptionRepo = RepoManager.SubscriptionRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var balanceRepo = RepoManager.BalanceChangedEventRepository;

        var owner = await DbHelper.AddDefaultUser("IncomeOwner");
        var subscriber = await DbHelper.AddDefaultUser("IncomeSubscriber");
        Assert.That(owner?.Id, Is.Not.Null);
        Assert.That(subscriber?.Id, Is.Not.Null);

        var canvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = owner!.Id!.Value,
            Name = "Hourly Income Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, "testpassword");
        Assert.That(canvas, Is.Not.Null);

        await subscriptionRepo.Subscribe(owner.Id.Value, canvas!.Id, "testpassword");
        await subscriptionRepo.Subscribe(subscriber!.Id!.Value, canvas.Id, "testpassword");
        await balanceRepo.TryChangeBalanceAsync(owner.Id.Value, canvas.Id, 100, BalanceChangedReason.Regular);

        var colorBlack = (await RepoManager.ColorRepository.GetAllAsync()).First(x => x.Name == "Black");
        for (var i = 0; i < 3; i++)
        {
            var changedPixel = await pixelRepo.TryChangePixelAsync(owner.Id.Value, new PixelDto
            {
                CanvasId = canvas.Id,
                X = i,
                Y = i,
                ColorId = colorBlack.Id,
                Price = 1,
            });

            Assert.That(changedPixel, Is.Not.Null);
        }

        var processor = new HourlyCanvasIncomeProcessor(
            DbContext,
            RepoManager,
            DbHelper.LoggerFactoryInterface.CreateLogger<HourlyCanvasIncomeProcessor>());

        var batches = await processor.ProcessAsync();
        var batch = batches.Single(x => x.CanvasId == canvas.Id);
        Assert.That(batch.CanvasId, Is.EqualTo(canvas.Id));
        Assert.That(batch.CanvasName, Is.EqualTo(canvas.Name));
        Assert.That(batch.Updates.Count, Is.EqualTo(2));

        var ownerUpdate = batch.Updates.Single(x => x.UserName == owner.UserName);
        var subscriberUpdate = batch.Updates.Single(x => x.UserName == subscriber.UserName);

        Assert.That(ownerUpdate.Amount, Is.EqualTo(30));
        Assert.That(ownerUpdate.NewBalance, Is.EqualTo(128));
        Assert.That(subscriberUpdate.Amount, Is.EqualTo(10));
        Assert.That(subscriberUpdate.NewBalance, Is.EqualTo(11));

        var ownerEvents = (await balanceRepo.GetByUserAndCanvasIdAsync(owner.Id.Value, canvas.Id)).ToList();
        var subscriberEvents = (await balanceRepo.GetByUserAndCanvasIdAsync(subscriber.Id.Value, canvas.Id)).ToList();

        Assert.That(ownerEvents.First().Reason, Is.EqualTo(BalanceChangedReason.HourlyIncome));
        Assert.That(subscriberEvents.First().Reason, Is.EqualTo(BalanceChangedReason.HourlyIncome));
    }
}
