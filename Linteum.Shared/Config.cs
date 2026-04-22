using Linteum.Shared.DTO;

namespace Linteum.Shared;

public class Config
{
    public string DefaultCanvasName { get; set; } = "home";
    public List<string> SecondaryCanvasNames { get; set; } = new() { "VanGogh", "Thailand" };
    public string MasterPasswordHash { get; set; } = "MasterPasswordHash";
    public string GoogleClientId { get; set; } = string.Empty;
    
    public string DefaultPage { get; set; } = "/canvas/home";
    public int DefaultCanvasWidth { get; set; } = 1024;

    public int ExpiredSessionTimeoutMinutes { get; set; } = 60;
    public int DefaultCanvasHeight { get; set; } = 1024;
    
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
