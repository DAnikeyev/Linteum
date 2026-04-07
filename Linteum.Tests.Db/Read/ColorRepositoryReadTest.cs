namespace Linteum.Tests.Db.Read;

internal class ColorRepositoryReadTest : SyntheticDataTest
{
    [Test]
    public async Task GetColors()
    {
        var colors = (await RepoManager.ColorRepository.GetAllAsync());
        Assert.That(colors.Select(x => (x.Name, x.HexValue)), Is.EqualTo(DefaultConfig.Colors.Select(x => (x.Name, x.HexValue))));
    }
}