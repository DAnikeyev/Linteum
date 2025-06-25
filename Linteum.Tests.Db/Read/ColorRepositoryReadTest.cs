namespace Linteum.Tests.Read;

internal class ColorRepositoryReadTest : SyntheticDataTest
{
    [Test]
    public async Task GetColors()
    {
        var colors = (await RepoManager.ColorRepository.GetAllAsync());
        CollectionAssert.AreEqual(colors.Select(x => (x.Name, x.HexValue)), DefaultConfig.Colors.Select(x => (x.Name, x.HexValue)));
    }
}