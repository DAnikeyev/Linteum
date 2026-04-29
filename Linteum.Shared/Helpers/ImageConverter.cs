using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Linteum.Shared.DTO;

public class ImageConverter
{
    public static ColorDto[,] ConvertImageToGrid(string filepath, int width, int height, List<ColorDto> palette)
    {
        if (!File.Exists(filepath))
            throw new FileNotFoundException("Image file not found.", filepath);

        using var image = Image.Load<Rgba32>(filepath);
        return ConvertImageToGrid(image, width, height, palette);
    }

    public static ColorDto[,] ConvertImageToGrid(Image<Rgba32> image, int width, int height, List<ColorDto> palette)
    {
        image.Mutate(x => x.Resize(width, height));

        var grid = new ColorDto[width, height];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                for (var x = 0; x < accessor.Width; x++)
                {
                    Rgba32 pixelColor = pixelRow[x];
                    grid[x, y] = GetClosestColor(pixelColor, palette);
                }
            }
        });

        return grid;
    }

    private static ColorDto GetClosestColor(Rgba32 target, List<ColorDto> palette)
    {
        if (palette == null || palette.Count == 0)
            throw new ArgumentException("palette must contain at least one color", nameof(palette));

        ColorDto? closestMatch = null;
        var minDistanceSquared = double.MaxValue;

        foreach (var colorDto in palette)
        {
            var paletteColor = Rgba32.ParseHex(colorDto.HexValue.TrimStart('#'));

            var dr = (double)target.R - paletteColor.R;
            var dg = (double)target.G - paletteColor.G;
            var db = (double)target.B - paletteColor.B;

            var distanceSquared = dr * dr + dg * dg + db * db;

            if (distanceSquared == 0)
                return colorDto;

            if (distanceSquared < minDistanceSquared)
            {
                minDistanceSquared = distanceSquared;
                closestMatch = colorDto;
            }
        }

        return closestMatch ?? palette.First();
    }
}
