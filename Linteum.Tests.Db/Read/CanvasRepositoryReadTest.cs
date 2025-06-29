using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Tests.Db.Read;

internal class CanvasRepositoryReadTest : SyntheticDataTest
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
        
        var allPublicCanvases = (await canvasRepo.GetAllAsync()).ToList();
        var allCanvases = (await canvasRepo.GetAllAsync(true)).ToList();
        Assert.That(allPublicCanvases.Count, Is.EqualTo(2));
        Assert.That(allCanvases.Count, Is.EqualTo(3));
        Assert.That(allPublicCanvases.Any(c => c.Name == DefaultConfig.DefaultCanvasName), Is.True);
        Assert.That(allPublicCanvases.Any(c => c.Id == newCanvasWithoutPassword.Id), Is.True);
        Assert.That(allCanvases.Any(c => c.Id == newCanvasWithoutPassword.Id), Is.True);
        Assert.That(allCanvases.Any(c => c.Id == newCanvasWithPassword.Id), Is.True);
        Assert.That(allPublicCanvases.Any(c => c.Id == newCanvasWithPassword.Id), Is.False);
        
        var defaultCanvas = await canvasRepo.GetByNameAsync(DefaultConfig.DefaultCanvasName);
        var passwordCanvas = await canvasRepo.GetByNameAsync(newCanvasWithPassword.Name);
        var noPasswordCanvas = await canvasRepo.GetByNameAsync(newCanvasWithoutPassword.Name);
        var nonExistingCanvas = await canvasRepo.GetByNameAsync("RandomName");
        
        Assert.IsNotNull(defaultCanvas);
        Assert.That(defaultCanvas.Name, Is.EqualTo(DefaultConfig.DefaultCanvasName));
        Assert.That(defaultCanvas.Width, Is.EqualTo(DefaultConfig.DefaultCanvasWidth));
        Assert.That(defaultCanvas.Height, Is.EqualTo(DefaultConfig.DefaultCanvasHeight));
        
        Assert.IsNotNull(passwordCanvas);
        Assert.That(passwordCanvas.Name, Is.EqualTo(newCanvasWithPassword.Name));
        Assert.That(passwordCanvas.Width, Is.EqualTo(newCanvasWithPassword.Width));
        Assert.That(passwordCanvas.Height, Is.EqualTo(newCanvasWithPassword.Height));
        
        Assert.IsNotNull(noPasswordCanvas);
        Assert.That(noPasswordCanvas.Name, Is.EqualTo(newCanvasWithoutPassword.Name));
        Assert.That(noPasswordCanvas.Width, Is.EqualTo(newCanvasWithoutPassword.Width));
        Assert.That(noPasswordCanvas.Height, Is.EqualTo(newCanvasWithoutPassword.Height));
        
        Assert.IsNull(nonExistingCanvas);
    }
    
}