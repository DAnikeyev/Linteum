using Linteum.Shared.DTO;

namespace Linteum.Shared;

public class DbConfig
{
    public string DefaultCanvasName { get; set; } = "Home";
    public string MasterPasswordHash { get; set; } = "MasterPasswordHash";
    public int DefaultCanvasWidth { get; set; } = 24;

    public int DefaultCanvasHeight { get; set; } = 24;
    
    public List<ColorDto> Colors { get; set; } = new List<ColorDto>()
    {
        new ColorDto { HexValue = "#FF0000", Name = "Red" },
        new ColorDto { HexValue = "#00FF00", Name = "Lime" },
        new ColorDto { HexValue = "#0000FF", Name = "Blue" },
        new ColorDto { HexValue = "#FFFF00", Name = "Yellow" },
        new ColorDto { HexValue = "#FFA500", Name = "Orange" },
        new ColorDto { HexValue = "#800080", Name = "Purple" },
        new ColorDto { HexValue = "#00FFFF", Name = "Cyan" },
        new ColorDto { HexValue = "#FFC0CB", Name = "Pink" },
        new ColorDto { HexValue = "#A52A2A", Name = "Brown" },
        new ColorDto { HexValue = "#808080", Name = "Gray" },
        new ColorDto { HexValue = "#000000", Name = "Black" },
        new ColorDto { HexValue = "#FFFFFF", Name = "White" },
        new ColorDto { HexValue = "#008000", Name = "Green" },
        new ColorDto { HexValue = "#FFD700", Name = "Gold" },
        new ColorDto { HexValue = "#C0C0C0", Name = "Silver" },
        new ColorDto { HexValue = "#ADD8E6", Name = "Light Blue" },
        new ColorDto { HexValue = "#800000", Name = "Maroon" },
        new ColorDto { HexValue = "#FF00FF", Name = "Magenta" },
        new ColorDto { HexValue = "#008080", Name = "Teal" },
        new ColorDto { HexValue = "#F5DEB3", Name = "Wheat" }
    };
}
