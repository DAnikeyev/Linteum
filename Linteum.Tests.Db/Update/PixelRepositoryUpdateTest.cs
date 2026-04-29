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
            CreatorId = user!.Id!.Value,
            Name = "Test Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
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
            CreatorId = user!.Id!.Value,
            Name = "Test Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
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

    [Test]
    public async Task MoneyOnDifferentCanvas_ExpectNoChange()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var subscriptionRepo = RepoManager.SubscriptionRepository;
        var user = await DbHelper.AddDefaultUser("User1");

        var targetCanvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Target Economy Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, "testpassword");
        Assert.IsNotNull(targetCanvas);

        var fundedCanvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = user.Id.Value,
            Name = "Funded Economy Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, "testpassword");
        Assert.IsNotNull(fundedCanvas);

        var userId = user!.Id!.Value;
        var targetCanvasId = targetCanvas!.Id;
        var fundedCanvasId = fundedCanvas!.Id;
        await subscriptionRepo.Subscribe(userId, targetCanvasId, "testpassword");
        await subscriptionRepo.Subscribe(userId, fundedCanvasId, "testpassword");
        await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(userId, fundedCanvasId, 1000, BalanceChangedReason.Regular);

        var colorBlack = (await RepoManager.ColorRepository.GetAllAsync()).First(x => x.Name == "Black");
        var pixelDto = new PixelDto
        {
            CanvasId = targetCanvasId,
            X = 5,
            Y = 5,
            ColorId = colorBlack.Id,
            Price = 2,
        };

        var changedPixel = await pixelRepo.TryChangePixelAsync(userId, pixelDto);
        Assert.IsNull(changedPixel);
    }

    [Test]
    public async Task FirstEconomyPixelChange_UsesDefaultColorInHistory()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var subscriptionRepo = RepoManager.SubscriptionRepository;
        var pixelChangedEventRepo = RepoManager.PixelChangedEventRepository;
        var user = await DbHelper.AddDefaultUser("User1");

        var canvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "History Economy Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, "testpassword");
        Assert.IsNotNull(canvas);

        await subscriptionRepo.Subscribe(user.Id.Value, canvas!.Id, "testpassword");
        await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(user.Id.Value, canvas.Id, 1000, BalanceChangedReason.Regular);

        var colors = (await RepoManager.ColorRepository.GetAllAsync()).ToList();
        var defaultColor = colors.First(x => x.Name == "White");
        var colorBlack = colors.First(x => x.Name == "Black");

        var changedPixel = await pixelRepo.TryChangePixelAsync(user.Id.Value, new PixelDto
        {
            CanvasId = canvas.Id,
            X = 5,
            Y = 5,
            ColorId = colorBlack.Id,
            Price = 2,
        });

        Assert.IsNotNull(changedPixel);
        Assert.That(changedPixel!.Id, Is.Not.Null);

        var history = (await pixelChangedEventRepo.GetByPixelIdAsync(changedPixel.Id!.Value)).ToList();
        Assert.That(history.Count, Is.EqualTo(1));
        Assert.That(history[0].OldOwnerUserId, Is.Null);
        Assert.That(history[0].OldColorId, Is.EqualTo(defaultColor.Id));
        Assert.That(history[0].NewColorId, Is.EqualTo(colorBlack.Id));
        Assert.That(history[0].NewPrice, Is.EqualTo(2));
    }

    [Test]
    public async Task CoordinatesEqualToCanvasBounds_ExpectNoChange()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var subscriptionRepo = RepoManager.SubscriptionRepository;
        var user = await DbHelper.AddDefaultUser("User1");

        var canvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Bounded Economy Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, "testpassword");
        Assert.IsNotNull(canvas);

        await subscriptionRepo.Subscribe(user.Id.Value, canvas!.Id, "testpassword");
        await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(user.Id.Value, canvas.Id, 1000, BalanceChangedReason.Regular);

        var colorBlack = (await RepoManager.ColorRepository.GetAllAsync()).First(x => x.Name == "Black");
        var changedPixel = await pixelRepo.TryChangePixelAsync(user.Id.Value, new PixelDto
        {
            CanvasId = canvas.Id,
            X = canvas.Width,
            Y = canvas.Height,
            ColorId = colorBlack.Id,
            Price = 2,
        });

        Assert.IsNull(changedPixel);
    }

    [Test]
    public async Task NormalCanvas_DailyLimitIsTrackedPerCanvas()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var pixelRepo = RepoManager.PixelRepository;
        var user = await DbHelper.AddDefaultUser("NormalQuotaUser");
        var colorBlack = (await RepoManager.ColorRepository.GetAllAsync()).First(x => x.Name == "Black");

        var canvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Normal Quota Canvas",
            Width = 200,
            Height = 1,
            CanvasMode = CanvasMode.Normal,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var secondCanvas = await canvasRepo.TryAddCanvas(new CanvasDto
        {
            CreatorId = user.Id.Value,
            Name = "Normal Quota Canvas 2",
            Width = 200,
            Height = 1,
            CanvasMode = CanvasMode.Normal,
        }, passwordHash: null);

        Assert.That(secondCanvas, Is.Not.Null);

        for (var x = 0; x < DefaultConfig.NormalModeDailyPixelLimit; x++)
        {
            var changedPixel = await pixelRepo.TryChangePixelAsync(user.Id.Value, new PixelDto
            {
                CanvasId = canvas!.Id,
                X = x,
                Y = 0,
                ColorId = colorBlack.Id,
                Price = 0,
            });

            Assert.That(changedPixel, Is.Not.Null, $"Expected pixel change {x + 1} to succeed.");
        }

        var rejectedPixel = await pixelRepo.TryChangePixelAsync(user.Id.Value, new PixelDto
        {
            CanvasId = canvas!.Id,
            X = DefaultConfig.NormalModeDailyPixelLimit,
            Y = 0,
            ColorId = colorBlack.Id,
            Price = 0,
        });

        var secondCanvasPixel = await pixelRepo.TryChangePixelAsync(user.Id.Value, new PixelDto
        {
            CanvasId = secondCanvas!.Id,
            X = 0,
            Y = 0,
            ColorId = colorBlack.Id,
            Price = 0,
        });

        var quota = await pixelRepo.GetNormalModeQuotaAsync(user.Id.Value, canvas.Id);
        var secondCanvasQuota = await pixelRepo.GetNormalModeQuotaAsync(user.Id.Value, secondCanvas.Id);

        Assert.That(rejectedPixel, Is.Null);
        Assert.That(secondCanvasPixel, Is.Not.Null);
        Assert.That(quota.IsEnforced, Is.True);
        Assert.That(quota.UsedToday, Is.EqualTo(DefaultConfig.NormalModeDailyPixelLimit));
        Assert.That(quota.RemainingToday, Is.EqualTo(0));
        Assert.That(secondCanvasQuota.IsEnforced, Is.True);
        Assert.That(secondCanvasQuota.UsedToday, Is.EqualTo(1));
        Assert.That(secondCanvasQuota.RemainingToday, Is.EqualTo(DefaultConfig.NormalModeDailyPixelLimit - 1));
    }

    [Test]
    public async Task TryChangePixelsBatchAsync_DeduplicatesMatchingCoordinates()
    {
        var user = await DbHelper.AddDefaultUser("BatchDedupUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Batch Dedup Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.FreeDraw,
        }, passwordHash: null);

        var colors = (await RepoManager.ColorRepository.GetAllAsync()).ToList();
        var black = colors.First(color => color.Name == "Black");
        var red = colors.First(color => color.Name == "Red");

        var result = await RepoManager.PixelRepository.TryChangePixelsBatchAsync(user.Id.Value,
        [
            new PixelDto { CanvasId = canvas!.Id, X = 1, Y = 1, ColorId = black.Id },
            new PixelDto { CanvasId = canvas.Id, X = 1, Y = 1, ColorId = red.Id },
            new PixelDto { CanvasId = canvas.Id, X = 2, Y = 1, ColorId = black.Id },
        ]);

        Assert.That(result.RequestedCount, Is.EqualTo(3));
        Assert.That(result.DeduplicatedCount, Is.EqualTo(2));
        Assert.That(result.ChangedPixels.Count, Is.EqualTo(2));

        var persistedPixels = (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id))
            .OrderBy(pixel => pixel.Y)
            .ThenBy(pixel => pixel.X)
            .ToList();

        Assert.That(persistedPixels.Count, Is.EqualTo(2));
        Assert.That(persistedPixels.Single(pixel => pixel.X == 1 && pixel.Y == 1).ColorId, Is.EqualTo(red.Id));
    }

    [Test]
    public async Task TryChangePixelsBatchAsync_EconomyStopsWhenBudgetRunsOut()
    {
        var user = await DbHelper.AddDefaultUser("BatchEconomyBudgetUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Batch Economy Budget Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, "testpassword");

        await RepoManager.SubscriptionRepository.Subscribe(user.Id.Value, canvas!.Id, "testpassword");
        await RepoManager.BalanceChangedEventRepository.TryChangeBalanceAsync(user.Id.Value, canvas.Id, 2, BalanceChangedReason.Regular);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var availableGold = (await RepoManager.BalanceChangedEventRepository.GetByUserAndCanvasIdAsync(user.Id.Value, canvas.Id))
            .OrderByDescending(item => item.ChangedAt)
            .Select(item => item.NewBalance)
            .First();
        var pixels = Enumerable.Range(0, (int)availableGold + 1)
            .Select(x => new PixelDto
            {
                CanvasId = canvas.Id,
                X = x,
                Y = 0,
                ColorId = black.Id,
                Price = 1,
            })
            .ToList();

        var result = await RepoManager.PixelRepository.TryChangePixelsBatchAsync(user.Id.Value, pixels);

        Assert.That(result.ChangedPixels.Count, Is.EqualTo((int)availableGold));
        Assert.That(result.StoppedByBudget, Is.True);
        Assert.That(result.StoppedByNormalModeLimit, Is.False);
    }

    [Test]
    public async Task TryChangePixelsBatchAsync_NormalStopsAtDailyLimit()
    {
        var user = await DbHelper.AddDefaultUser("BatchNormalQuotaUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Batch Normal Quota Canvas",
            Width = 200,
            Height = 1,
            CanvasMode = CanvasMode.Normal,
        }, passwordHash: null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        for (var x = 0; x < DefaultConfig.NormalModeDailyPixelLimit - 1; x++)
        {
            var changedPixel = await RepoManager.PixelRepository.TryChangePixelAsync(user.Id.Value, new PixelDto
            {
                CanvasId = canvas!.Id,
                X = x,
                Y = 0,
                ColorId = black.Id,
                Price = 0,
            });

            Assert.That(changedPixel, Is.Not.Null);
        }

        var result = await RepoManager.PixelRepository.TryChangePixelsBatchAsync(user.Id.Value,
        [
            new PixelDto { CanvasId = canvas!.Id, X = DefaultConfig.NormalModeDailyPixelLimit - 1, Y = 0, ColorId = black.Id },
            new PixelDto { CanvasId = canvas.Id, X = DefaultConfig.NormalModeDailyPixelLimit, Y = 0, ColorId = black.Id },
        ]);

        Assert.That(result.ChangedPixels.Count, Is.EqualTo(1));
        Assert.That(result.StoppedByNormalModeLimit, Is.True);
        Assert.That(result.StoppedByBudget, Is.False);
    }

    [Test]
    public async Task TryDeletePixelsBatchAsync_ReturnsOnlyConfirmedDeletedCoordinates()
    {
        var user = await DbHelper.AddDefaultUser("BatchDeleteUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Batch Delete Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.FreeDraw,
        }, passwordHash: null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var paintResult = await RepoManager.PixelRepository.TryChangePixelsBatchAsync(user.Id.Value,
        [
            new PixelDto { CanvasId = canvas!.Id, X = 1, Y = 1, ColorId = black.Id },
            new PixelDto { CanvasId = canvas.Id, X = 3, Y = 2, ColorId = black.Id },
        ]);

        Assert.That(paintResult.ChangedPixels.Count, Is.EqualTo(2));

        var deleteResult = await RepoManager.PixelRepository.TryDeletePixelsBatchAsync(user.Id.Value,
        [
            new CoordinateDto { X = 1, Y = 1 },
            new CoordinateDto { X = 9, Y = 9 },
            new CoordinateDto { X = 3, Y = 2 },
            new CoordinateDto { X = 1, Y = 1 },
        ], canvas.Id);

        Assert.That(deleteResult.DeletedCount, Is.EqualTo(2));
        Assert.That(
            deleteResult.DeletedCoordinates.Select(coordinate => (coordinate.X, coordinate.Y)).ToArray(),
            Is.EquivalentTo(new[] { (1, 1), (3, 2) }));

        var remainingPixels = (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id)).ToList();
        Assert.That(remainingPixels.Any(pixel => (pixel.X, pixel.Y) is (1, 1) or (3, 2)), Is.False);
    }

    [Test]
    public async Task TryChangePixelsBatchAsync_MasterOverrideBypassesEconomyBudgetAndNormalLimit()
    {
        var user = await DbHelper.AddDefaultUser("BatchMasterOverrideUser");
        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");

        var economyCanvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Batch Master Economy Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, "testpassword");

        await RepoManager.SubscriptionRepository.Subscribe(user.Id.Value, economyCanvas!.Id, "testpassword");

        var economyResult = await RepoManager.PixelRepository.TryChangePixelsBatchAsync(user.Id.Value,
        [
            new PixelDto { CanvasId = economyCanvas.Id, X = 0, Y = 0, ColorId = black.Id, Price = 0 },
            new PixelDto { CanvasId = economyCanvas.Id, X = 1, Y = 0, ColorId = black.Id, Price = 0 },
        ], useMasterOverride: true);

        Assert.That(economyResult.ChangedPixels.Count, Is.EqualTo(2));
        Assert.That(economyResult.ChangedPixels.All(pixel => pixel.Price == 1), Is.True);

        var normalCanvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user.Id.Value,
            Name = "Batch Master Normal Canvas",
            Width = 200,
            Height = 1,
            CanvasMode = CanvasMode.Normal,
        }, passwordHash: null);

        for (var x = 0; x < DefaultConfig.NormalModeDailyPixelLimit; x++)
        {
            var changedPixel = await RepoManager.PixelRepository.TryChangePixelAsync(user.Id.Value, new PixelDto
            {
                CanvasId = normalCanvas!.Id,
                X = x,
                Y = 0,
                ColorId = black.Id,
            });

            Assert.That(changedPixel, Is.Not.Null);
        }

        var normalResult = await RepoManager.PixelRepository.TryChangePixelsBatchAsync(user.Id.Value,
        [
            new PixelDto { CanvasId = normalCanvas!.Id, X = DefaultConfig.NormalModeDailyPixelLimit, Y = 0, ColorId = black.Id },
        ], useMasterOverride: true);

        Assert.That(normalResult.ChangedPixels.Count, Is.EqualTo(1));
        Assert.That(normalResult.StoppedByNormalModeLimit, Is.False);
    }
}
