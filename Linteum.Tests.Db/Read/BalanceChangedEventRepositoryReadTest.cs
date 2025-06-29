using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Create;

internal class BalanceChangedEventRepositoryReadTest : SyntheticDataTest
{
    [Test]
    public async Task GetBalance()
    {
        var user = await DbHelper.AddDefaultUser("U1");
        var user2 = await DbHelper.AddDefaultUser("U2");
        var user3 = await DbHelper.AddDefaultUser("U3");
        var canvas = (await RepoManager.CanvasRepository.GetAllAsync()).FirstOrDefault();
        var BCERepo = RepoManager.BalanceChangedEventRepository;
        var balance1 = await BCERepo.TryChangeBalanceAsync(user.Id.Value, canvas!.Id, 1000, BalanceChangedReason.Regular);
        var balance2 = await BCERepo.TryChangeBalanceAsync(user.Id.Value, canvas!.Id, -600, BalanceChangedReason.Regular);
        var balance3 = await BCERepo.TryChangeBalanceAsync(user.Id.Value, canvas!.Id, -600, BalanceChangedReason.Regular);
        var balance4 = await BCERepo.TryChangeBalanceAsync(user.Id.Value, canvas!.Id, 1000, BalanceChangedReason.Regular);
        var balance_user2 = await BCERepo.TryChangeBalanceAsync(user2.Id.Value, canvas!.Id, 1000, BalanceChangedReason.Regular);
        
        Assert.That(balance1!.NewBalance, Is.EqualTo(1001));
        Assert.That(balance2!.NewBalance, Is.EqualTo(401));
        Assert.That(balance4!.NewBalance, Is.EqualTo(1401));
        Assert.That(balance_user2!.NewBalance, Is.EqualTo(1001));
        
        Assert.That(balance1.CanvasId, Is.EqualTo(canvas!.Id));
        Assert.That(balance1.OldBalance, Is.EqualTo(1));
        Assert.That(balance1.UserId, Is.EqualTo(user.Id.Value));
        Assert.That(balance1.Reason, Is.EqualTo(BalanceChangedReason.Regular));
        
        var balanceChangedEvents = await BCERepo.GetByUserIdAsync(user.Id.Value);
        Assert.That(balanceChangedEvents.Count(), Is.EqualTo(4));
        var balanceChangedEvents2 = await BCERepo.GetByUserAndCanvasIdAsync(user.Id.Value, canvas!.Id);
        Assert.That(balanceChangedEvents2.Count(), Is.EqualTo(4));
        var balanceChangedEvents3 = await BCERepo.GetByUserIdAsync(user2.Id.Value);
        Assert.That(balanceChangedEvents3.Count(), Is.EqualTo(2));
        var balanceChangedEvents4 = await BCERepo.GetByUserAndCanvasIdAsync(user3.Id.Value, canvas!.Id);
        Assert.That(balanceChangedEvents4.Count(), Is.EqualTo(1));
    }
    
}