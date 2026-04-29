using Linteum.Shared.DTO;

namespace Linteum.Shared;

public class Config
{
    public string DefaultCanvasName { get; set; } = "home";
    public List<string> SecondaryDefaultCanvasNames { get; set; } = new() { "home_FreeDraw", "home_Economy", "VanGogh", "Thailand" };
    public string MasterPasswordHash { get; set; } = "MasterPasswordHash";
    public string GoogleClientId { get; set; } = string.Empty;
    
    public string DefaultPage { get; set; } = "/canvas/home";
    public int DefaultCanvasWidth { get; set; } = 1024;

    public int ExpiredSessionTimeoutMinutes { get; set; } = 60;
    public int DefaultCanvasHeight { get; set; } = 1024;
    public int NormalModeDailyPixelLimit { get; set; } = 100;

    public List<CanvasDto> SeedCanvases { get; set; } = new()
    {
        new CanvasDto
        {
            Name = "home",
            Width = 1024,
            Height = 1024,
            CanvasMode = CanvasMode.Normal,
        },
        new CanvasDto
        {
            Name = "home_FreeDraw",
            Width = 1024,
            Height = 1024,
            CanvasMode = CanvasMode.FreeDraw,
        },
        new CanvasDto
        {
            Name = "home_Economy",
            Width = 1024,
            Height = 1024,
            CanvasMode = CanvasMode.Economy,
        }
    };

    public IReadOnlyCollection<string> GetProtectedCanvasNames() =>
        SeedCanvases
            .Select(canvas => canvas.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyCollection<CanvasDto> GetDefaultCanvases()
    {
        var seedCanvases = SeedCanvases
            .Where(canvas => !string.IsNullOrWhiteSpace(canvas.Name))
            .Select(canvas => new CanvasDto
            {
                Name = canvas.Name,
                Width = string.Equals(canvas.Name, DefaultCanvasName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(canvas.Name, "home_FreeDraw", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(canvas.Name, "home_Economy", StringComparison.OrdinalIgnoreCase)
                    ? DefaultCanvasWidth
                    : canvas.Width,
                Height = string.Equals(canvas.Name, DefaultCanvasName, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(canvas.Name, "home_FreeDraw", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(canvas.Name, "home_Economy", StringComparison.OrdinalIgnoreCase)
                    ? DefaultCanvasHeight
                    : canvas.Height,
                CanvasMode = canvas.CanvasMode,
                CreatorId = canvas.CreatorId,
                CreatedAt = canvas.CreatedAt,
                UpdatedAt = canvas.UpdatedAt,
            })
            .ToList();

        if (seedCanvases.Count > 0)
        {
            return seedCanvases;
        }

        return new[]
        {
            new CanvasDto
            {
                Name = DefaultCanvasName,
                Width = DefaultCanvasWidth,
                Height = DefaultCanvasHeight,
                CanvasMode = CanvasMode.Normal,
            }
        };
    }
    
    public List<ColorDto> Colors { get; set; } = new List<ColorDto>()
    {
        new ColorDto { HexValue = "#FF0000", Name = "Red" },
        new ColorDto { HexValue = "#00FF00", Name = "Lime" },
        new ColorDto { HexValue = "#80FD18", Name = "Neon Lime" },
        new ColorDto { HexValue = "#0000FF", Name = "Blue" },
        new ColorDto { HexValue = "#0B7AFE", Name = "Azure" },
        new ColorDto { HexValue = "#FFFF00", Name = "Yellow" },
        new ColorDto { HexValue = "#FFA500", Name = "Orange" },
        new ColorDto { HexValue = "#800080", Name = "Purple" },
        new ColorDto { HexValue = "#7D3FFF", Name = "Electric Violet" },
        new ColorDto { HexValue = "#00FFFF", Name = "Cyan" },
        new ColorDto { HexValue = "#FFC0CB", Name = "Pink" },
        new ColorDto { HexValue = "#FE0A81", Name = "Hot Pink" },
        new ColorDto { HexValue = "#A52A2A", Name = "Brown" },
        new ColorDto { HexValue = "#C0C0C0", Name = "Silver" },
        new ColorDto { HexValue = "#808080", Name = "Gray" },
        new ColorDto { HexValue = "#000000", Name = "Black" },
        new ColorDto { HexValue = "#FFFFFF", Name = "White" },
        new ColorDto { HexValue = "#008000", Name = "Green" },
        new ColorDto { HexValue = "#FFD700", Name = "Gold" },
        new ColorDto { HexValue = "#ADD8E6", Name = "Light Blue" },
        new ColorDto { HexValue = "#FF00FF", Name = "Magenta" },
        new ColorDto { HexValue = "#008080", Name = "Teal" },
        new ColorDto { HexValue = "#F5DEB3", Name = "Wheat" },
        new ColorDto { HexValue = "#E6E6FA", Name = "Lavender" },
        new ColorDto { HexValue = "#FF7F50", Name = "Coral" },
        new ColorDto { HexValue = "#808000", Name = "Olive" },
        new ColorDto { HexValue = "#000080", Name = "Navy" },
        new ColorDto { HexValue = "#40E0D0", Name = "Turquoise" },
        new ColorDto { HexValue = "#FFFDD0", Name = "Cream" },
        new ColorDto { HexValue = "#B0E0E6", Name = "Powder Blue" },
        new ColorDto { HexValue = "#E9967A", Name = "Dark Salmon" },
        new ColorDto { HexValue = "#98FB98", Name = "Pale Green" },
        new ColorDto { HexValue = "#BC8F8F", Name = "Rosy Brown" },
        new ColorDto { HexValue = "#ACB33E", Name = "Olive Drab" },
        new ColorDto { HexValue = "#E166FA", Name = "Light Orchid" },
        new ColorDto { HexValue = "#E1FB5A", Name = "Lemon Lime" }
    };
}
