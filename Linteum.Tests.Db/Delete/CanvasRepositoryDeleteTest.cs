using Linteum.Shared.DTO;

namespace Linteum.Tests.Delete;

internal class CanvasRepositoryDeleteTest : SyntheticDataTest
{
    [Test]
    public async Task TryDeleteCanvas()
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
        
        var allPublicCanvases = (await canvasRepo.GetAllAsync()).ToList();
        var allCanvases = (await canvasRepo.GetAllAsync(true)).ToList();
        Assert.That(allPublicCanvases.Count, Is.EqualTo(2));
        Assert.That(allCanvases.Count, Is.EqualTo(3));
        
        var deleteWithWrongPassword = await canvasRepo.TryDeleteCanvasByName(newCanvasWithPassword.Name, "Please");
        Assert.IsFalse(deleteWithWrongPassword);
        
        var deleteDefault = await canvasRepo.TryDeleteCanvasByName(DefaultConfig.DefaultCanvasName, DefaultConfig.MasterPasswordHash);
        Assert.IsFalse(deleteDefault);
        
        var deleteWithPassword = await canvasRepo.TryDeleteCanvasByName(newCanvasWithPassword.Name, password);
        Assert.IsTrue(deleteWithPassword);
        
        var deleteWithoutPassword = await canvasRepo.TryDeleteCanvasByName(newCanvasWithoutPassword.Name, DefaultConfig.MasterPasswordHash);
        Assert.True(deleteWithoutPassword);
        
        
        allCanvases = (await canvasRepo.GetAllAsync(true)).ToList();
        Assert.That(allCanvases.Count, Is.EqualTo(1));
    }
    
}