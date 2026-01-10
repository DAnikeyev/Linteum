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
        ColorDto? closestMatch = null;
        double minDistanceSquared = double.MaxValue;

        foreach (var colorDto in palette)
        {
            var paletteColor = Rgba32.ParseHex(colorDto.HexValue.TrimStart('#'));

            double distanceSquared = Math.Pow(target.R - paletteColor.R, 2) +
                                     Math.Pow(target.G - paletteColor.G, 2) +
                                     Math.Pow(target.B - paletteColor.B, 2);

            if (distanceSquared < minDistanceSquared)
            {
                minDistanceSquared = distanceSquared;
                closestMatch = colorDto;
            }
        }

        return closestMatch ?? palette.First();
    }
}
