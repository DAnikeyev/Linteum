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

        // Track a fallback nearest color in case of any numerical edge-cases
        ColorDto? closestMatch = null;
        var minDistanceSquared = double.MaxValue;

        // Preallocate weights to avoid reallocation during selection
        var weights = new double[palette.Count];
        var totalWeight = 0.0;

        for (var i = 0; i < palette.Count; i++)
        {
            var colorDto = palette[i];
            var paletteColor = Rgba32.ParseHex(colorDto.HexValue.TrimStart('#'));

            var dr = (double)target.R - paletteColor.R;
            var dg = (double)target.G - paletteColor.G;
            var db = (double)target.B - paletteColor.B;

            var distanceSquared = dr * dr + dg * dg + db * db;

            // If there's an exact match, return immediately (deterministic)
            if (distanceSquared == 0)
                return colorDto;

            if (distanceSquared < minDistanceSquared)
            {
                minDistanceSquared = distanceSquared;
                closestMatch = colorDto;
            }

            // Weight decreases with distance. Use an exponential falloff: e^-(sqrt(distanceSquared))/10
            var weight = System.Math.Exp(-System.Math.Sqrt(distanceSquared) / 3.0);
            weights[i] = weight;
            totalWeight += weight;
        }

        // Fallback: if total weight is zero for some reason, return the nearest match
        if (totalWeight <= 0 || double.IsInfinity(totalWeight) || double.IsNaN(totalWeight))
            return closestMatch ?? palette.First();

        // Select a random value in [0, totalWeight)
        var r = Random.Shared.NextDouble() * totalWeight;

        var acc = 0.0;
        for (var i = 0; i < palette.Count; i++)
        {
            acc += weights[i];
            if (r < acc)
                return palette[i];
        }

        // Shouldn't get here, but return the nearest color as a safe fallback
        return closestMatch ?? palette.First();
    }
}
