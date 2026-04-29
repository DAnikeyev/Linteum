using Linteum.Shared;
using Microsoft.AspNetCore.Http;

namespace Linteum.Api.Models;

public class CanvasImageUploadForm
{
    public string Name { get; set; } = string.Empty;
    public CanvasMode CanvasMode { get; set; } = CanvasMode.Normal;
    public string? PasswordHash { get; set; }
    public IFormFile? Image { get; set; }
}
