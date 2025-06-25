using System.ComponentModel.DataAnnotations;

namespace Linteum.Domain;

public class Color
{
    public int Id { get; set; }
    
    [MaxLength(7)] // For "#RRGGBB"
    [RegularExpression("^#[0-9A-Fa-f]{6}$")]
    public string HexValue { get; set; } = null!;
    
    [MaxLength(64)]
    public string? Name { get; set; } // Optional, e.g. "Red"
}