using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Create;

internal class CanvasRepositoryCreateTest : SyntheticDataTest
{
    [Test]
    public async Task TryAddCanvas()
    {
        var canvasRepo = RepoManager.CanvasRepository;
        var canvasWithPassword = new CanvasDto
        {
            Name = "Test Canvas",
            Width = 10,
            Height = 10,
        };
        var password = "testpassword";
        var canvasWithoutPassword = new CanvasDto
        {
            Name = "Test Canvas No Password",
            Width = 10,
            Height = 10,
        };
        
        var newCanvasWithPassword = await canvasRepo.TryAddCanvas(canvasWithPassword, password);
        Assert.IsNotNull(newCanvasWithPassword);
        var newCanvasWithoutPassword = await canvasRepo.TryAddCanvas(canvasWithoutPassword, null);
        Assert.IsNotNull(newCanvasWithoutPassword);
    }
    
}