using Linteum.Domain;

namespace Linteum.Tests.Db.Read;

internal class ColorRepositoryReadTest : SyntheticDataTest
{
    [Test]
    public async Task GetColors()
    {
        var colors = (await RepoManager.ColorRepository.GetAllAsync());
        Assert.That(colors.Select(x => (x.Name, x.HexValue)), Is.EqualTo(DefaultConfig.Colors.Select(x => (x.Name, x.HexValue))));
    }

    [Test]
    public async Task GetColors_ReturnsConfiguredOrder_ForExistingRows()
    {
        DbContext.Colors.RemoveRange(DbContext.Colors);
        await DbContext.SaveChangesAsync();

        await DbContext.Colors.AddRangeAsync(DefaultConfig.Colors
            .AsEnumerable()
            .Reverse()
            .Select(color => new Color
            {
                HexValue = color.HexValue,
                Name = color.Name
            }));
        await DbContext.SaveChangesAsync();

        var colors = await RepoManager.ColorRepository.GetAllAsync();

        Assert.That(colors.Select(x => (x.Name, x.HexValue)), Is.EqualTo(DefaultConfig.Colors.Select(x => (x.Name, x.HexValue))));
    }
}
