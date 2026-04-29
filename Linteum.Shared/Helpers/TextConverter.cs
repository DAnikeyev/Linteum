using Linteum.Shared.DTO;
using System.Globalization;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Linteum.Shared.Helpers;

public class TextConverter
{
    private const float MinimumFontSize = 4f;
    private const float MaximumFontSize = 25f;
    private const float DefaultFontSize = 12f;

    public static ColorDto?[,] FromImage(ColorDto textColor, ColorDto? backgroundColor, string text, string fontSize)
    {
        ArgumentNullException.ThrowIfNull(textColor);
        ArgumentNullException.ThrowIfNull(text);

        var pixelFontSize = ParseFontSize(fontSize);
        var normalizedText = NormalizeText(text);
        var font = CreateFont(pixelFontSize);
        var textOptions = new TextOptions(font);
        var margin = GetMargin(pixelFontSize);
        var bounds = string.IsNullOrEmpty(normalizedText)
            ? FontRectangle.Empty
            : TextMeasurer.MeasureBounds(normalizedText, textOptions);
        var lineHeight = GetLineHeight(textOptions);
        var textWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var textHeight = Math.Max(lineHeight, (int)Math.Ceiling(bounds.Height));
        var width = Math.Max(textWidth + (margin * 2), (margin * 2) + 1);
        var height = Math.Max(textHeight + (margin * 2), (margin * 2) + lineHeight);

        using var mask = new Image<Rgba32>(width, height, Color.Transparent);

        if (!string.IsNullOrEmpty(normalizedText))
        {
            var origin = new PointF(margin - bounds.Left, margin - bounds.Top);
            mask.Mutate(context => context.DrawText(normalizedText, font, Color.White, origin));
        }

        var grid = new ColorDto?[width, height];

        if (backgroundColor != null)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    grid[x, y] = backgroundColor;
                }
            }
        }

        mask.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);

                for (var x = 0; x < accessor.Width; x++)
                {
                    if (pixelRow[x].A > 0)
                    {
                        grid[x, y] = textColor;
                    }
                }
            }
        });

        return grid;
    }

    private static string NormalizeText(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static float ParseFontSize(string fontSize)
    {
        if (string.IsNullOrWhiteSpace(fontSize))
        {
            return DefaultFontSize;
        }

        var trimmed = fontSize.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^2].Trim();
        }

        if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFontSize) &&
            !float.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out parsedFontSize))
        {
            parsedFontSize = DefaultFontSize;
        }

        return Math.Clamp(parsedFontSize, MinimumFontSize, MaximumFontSize);
    }

    private static Font CreateFont(float pixelFontSize)
    {
        foreach (var familyName in new[] { "Arial", "Segoe UI", "Tahoma", "Verdana", "DejaVu Sans", "Liberation Sans", "Noto Sans" })
        {
            if (SystemFonts.TryGet(familyName, out var family))
            {
                return family.CreateFont(pixelFontSize);
            }
        }

        using var familyEnumerator = SystemFonts.Collection.Families.GetEnumerator();
        if (familyEnumerator.MoveNext())
        {
            return familyEnumerator.Current.CreateFont(pixelFontSize);
        }

        throw new InvalidOperationException(
            "No system font families are available for text rendering. Install a system font package such as DejaVu Sans in the runtime environment.");
    }

    private static int GetMargin(float pixelFontSize) => Math.Max(2, (int)Math.Ceiling(pixelFontSize * 0.4f));

    private static int GetLineHeight(TextOptions textOptions)
    {
        var lineBounds = TextMeasurer.MeasureBounds("Ag", textOptions);
        return Math.Max(1, (int)Math.Ceiling(lineBounds.Height));
    }
}
