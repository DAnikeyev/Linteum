using Linteum.Shared.DTO;
using Linteum.Shared.Helpers;

namespace Linteum.Tests;

public class TextConverterTests
{
    private static readonly ColorDto TextColor = new() { Id = 1, HexValue = "#000000", Name = "Black" };
    private static readonly ColorDto BackgroundColor = new() { Id = 2, HexValue = "#FFFFFF", Name = "White" };

    [Test]
    public void FromImage_ClampsFontSizeBelowMinimum()
    {
        var tooSmall = TextConverter.FromImage(TextColor, BackgroundColor, "Test", "1");
        var minimum = TextConverter.FromImage(TextColor, BackgroundColor, "Test", "4");

        Assert.That(tooSmall.GetLength(0), Is.EqualTo(minimum.GetLength(0)));
        Assert.That(tooSmall.GetLength(1), Is.EqualTo(minimum.GetLength(1)));
        Assert.That(CountColor(tooSmall, TextColor.HexValue), Is.EqualTo(CountColor(minimum, TextColor.HexValue)));
    }

    [Test]
    public void FromImage_ClampsFontSizeAboveMaximum()
    {
        var tooLarge = TextConverter.FromImage(TextColor, BackgroundColor, "Test", "100");
        var maximum = TextConverter.FromImage(TextColor, BackgroundColor, "Test", "25");

        Assert.That(tooLarge.GetLength(0), Is.EqualTo(maximum.GetLength(0)));
        Assert.That(tooLarge.GetLength(1), Is.EqualTo(maximum.GetLength(1)));
        Assert.That(CountColor(tooLarge, TextColor.HexValue), Is.EqualTo(CountColor(maximum, TextColor.HexValue)));
    }

    [Test]
    public void FromImage_RespectsNewLinesAndKeepsTextAwayFromEdges()
    {
        var singleLine = TextConverter.FromImage(TextColor, BackgroundColor, "A", "12");
        var multiLine = TextConverter.FromImage(TextColor, BackgroundColor, "A\nA", "12");

        Assert.That(multiLine.GetLength(1), Is.GreaterThan(singleLine.GetLength(1)));
        Assert.That(RowContainsOnlyColor(multiLine, 0, BackgroundColor.HexValue), Is.True);
        Assert.That(RowContainsOnlyColor(multiLine, multiLine.GetLength(1) - 1, BackgroundColor.HexValue), Is.True);
        Assert.That(ColumnContainsOnlyColor(multiLine, 0, BackgroundColor.HexValue), Is.True);
        Assert.That(ColumnContainsOnlyColor(multiLine, multiLine.GetLength(0) - 1, BackgroundColor.HexValue), Is.True);
    }

    [Test]
    public void FromImage_UsesTransparentCellsWhenBackgroundColorIsNull()
    {
        var grid = TextConverter.FromImage(TextColor, null, "Hi", "12px");

        Assert.That(grid[0, 0], Is.Null);
        Assert.That(CountNulls(grid), Is.GreaterThan(0));
        Assert.That(CountColor(grid, TextColor.HexValue), Is.GreaterThan(0));
    }

    [Test]
    public void GetPreviewMetrics_ClampsFontSizeAndUsesExpectedMarginMath()
    {
        var tooSmall = TextConverter.GetPreviewMetrics("1");
        var minimum = TextConverter.GetPreviewMetrics("4");
        var tooLarge = TextConverter.GetPreviewMetrics("100");
        var maximum = TextConverter.GetPreviewMetrics("25");

        Assert.That(tooSmall.PixelFontSize, Is.EqualTo(minimum.PixelFontSize));
        Assert.That(tooSmall.Margin, Is.EqualTo(minimum.Margin));
        Assert.That(tooLarge.PixelFontSize, Is.EqualTo(maximum.PixelFontSize));
        Assert.That(tooLarge.Margin, Is.EqualTo(maximum.Margin));
        Assert.That(minimum.Margin, Is.EqualTo(2));
        Assert.That(maximum.Margin, Is.EqualTo(10));
        Assert.That(minimum.LineHeight, Is.GreaterThan(0));
        Assert.That(maximum.LineHeight, Is.GreaterThan(0));
    }

    private static int CountColor(ColorDto?[,] grid, string hexValue)
    {
        var count = 0;

        foreach (var cell in grid)
        {
            if (string.Equals(cell?.HexValue, hexValue, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountNulls(ColorDto?[,] grid)
    {
        var count = 0;

        foreach (var cell in grid)
        {
            if (cell == null)
            {
                count++;
            }
        }

        return count;
    }

    private static bool RowContainsOnlyColor(ColorDto?[,] grid, int row, string hexValue)
    {
        for (var x = 0; x < grid.GetLength(0); x++)
        {
            if (!string.Equals(grid[x, row]?.HexValue, hexValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ColumnContainsOnlyColor(ColorDto?[,] grid, int column, string hexValue)
    {
        for (var y = 0; y < grid.GetLength(1); y++)
        {
            if (!string.Equals(grid[column, y]?.HexValue, hexValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

