using Linteum.Infrastructure;
using Linteum.Shared;
using Microsoft.EntityFrameworkCore;

namespace Linteum.Tests.Read;

internal class DbSeederTests : SyntheticDataTest
{
    [Test]
    public async Task CanSeedDatabase()
    {
        var colors = await RepoManager.ColorRepository.GetAllAsync();
        var canvases = await RepoManager.CanvasRepository.GetAllAsync();
        Assert.IsNotNull(colors);
        Assert.IsNotNull(canvases);
        Assert.IsNotEmpty(colors);
        Assert.That(canvases.Count(), Is.EqualTo(1));
        var pixels = await RepoManager.PixelRepository.GetByCanvasIdAsync(canvases.First().Id);
        Assert.IsNotNull(pixels);
        Assert.That(pixels.Count(), Is.EqualTo(0));
    }
}