using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Create;

internal class BalanceChangedEventRepositoryCreateTest : SyntheticDataTest
{
    [Test]
    public async Task ChangeBalance()
    {
        var user = await DbHelper.AddDefaultUser("U1");
        var canvas = (await RepoManager.CanvasRepository.GetAllAsync()).FirstOrDefault();
        var BCERepo = RepoManager.BalanceChangedEventRepository;
        var balance1 = await BCERepo.TryChangeBalanceAsync(user.Id.Value, canvas!.Id, 1000, BalanceChangedReason.Regular);
        var balance2 = await BCERepo.TryChangeBalanceAsync(user.Id.Value, canvas!.Id, -600, BalanceChangedReason.Regular);
        var balance3 = await BCERepo.TryChangeBalanceAsync(user.Id.Value, canvas!.Id, -600, BalanceChangedReason.Regular);
        Assert.IsNotNull(balance1);
        Assert.IsNotNull(balance2);
        Assert.IsNull(balance3);
    }
    
}