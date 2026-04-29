using System.Globalization;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Services;

internal static class ColorPaletteOrdering
{
    public static List<ColorDto> SortByHue(IEnumerable<ColorDto> colors)
    {
        return colors
            .Select(color => new { Color = color, Hsv = ToHsv(color.HexValue) })
            .OrderBy(item => item.Hsv.H)
            .ThenBy(item => item.Hsv.S)
            .ThenBy(item => item.Hsv.V)
            .Select(item => item.Color)
            .ToList();
    }

    private static (double H, double S, double V) ToHsv(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return (0, 0, 0);
        }

        var normalizedHex = hex.TrimStart('#');
        if (normalizedHex.Length != 6)
        {
            return (0, 0, 0);
        }

        try
        {
            var red = int.Parse(normalizedHex.Substring(0, 2), NumberStyles.HexNumber);
            var green = int.Parse(normalizedHex.Substring(2, 2), NumberStyles.HexNumber);
            var blue = int.Parse(normalizedHex.Substring(4, 2), NumberStyles.HexNumber);

            var normalizedRed = red / 255d;
            var normalizedGreen = green / 255d;
            var normalizedBlue = blue / 255d;

            var max = Math.Max(normalizedRed, Math.Max(normalizedGreen, normalizedBlue));
            var min = Math.Min(normalizedRed, Math.Min(normalizedGreen, normalizedBlue));
            var delta = max - min;

            var hue = 0d;
            if (delta > 0)
            {
                if (Math.Abs(max - normalizedRed) < 0.0001)
                {
                    hue = 60 * (((normalizedGreen - normalizedBlue) / delta) % 6);
                }
                else if (Math.Abs(max - normalizedGreen) < 0.0001)
                {
                    hue = 60 * (((normalizedBlue - normalizedRed) / delta) + 2);
                }
                else if (Math.Abs(max - normalizedBlue) < 0.0001)
                {
                    hue = 60 * (((normalizedRed - normalizedGreen) / delta) + 4);
                }
            }

            if (hue < 0)
            {
                hue += 360;
            }

            var saturation = max == 0 ? 0 : delta / max;
            return (hue, saturation, max);
        }
        catch
        {
            return (0, 0, 0);
        }
    }
}

