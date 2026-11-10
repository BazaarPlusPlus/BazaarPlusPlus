#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

/// <summary>
/// The pixel work behind the kit's small art: where a hero's head is in a portrait, and an
/// area-averaged copy of a part of an image. Numeric data only, so it runs off the Unity thread.
/// Coordinates have their origin at the bottom left, as <see cref="PortraitImage"/> has.
/// </summary>
internal static class KitArtReduction
{
    /// <summary>A square part of an image, in its pixels; it may reach outside the image.</summary>
    internal readonly record struct Square(float Left, float Bottom, float Side);

    // A portrait well shows the head and shoulders: a square this tall, as a fraction of the
    // bust, hung just above the top of the head.
    private const float HeadShare = .6f;
    private const float HeadRoom = .04f;

    // The rows that find the head's centre: below the hair ornaments, above the shoulders.
    private const float HeadBandTop = .08f;
    private const float HeadBandBottom = .36f;

    /// <summary>
    /// The head and shoulders of a bust portrait: a square under the top of the art, centred on
    /// the head and not on the whole bust, which a weapon or a shoulder pulls to one side. Null
    /// when the image has no opaque pixel.
    /// </summary>
    internal static Square? HeadSquare(PortraitImage image)
    {
        int top = -1,
            bottom = -1;
        for (var y = 0; y < image.Height; y++)
        {
            if (!RowHasInk(image, y))
                continue;
            if (bottom < 0)
                bottom = y;
            top = y;
        }
        if (top < 0)
            return null;
        var height = top - bottom + 1;
        var bandTop = top - (int)(height * HeadBandTop);
        var bandBottom = top - (int)(height * HeadBandBottom);
        // Each row's longest opaque run is the head; a hair ornament or a raised weapon is a
        // shorter run beside it. The median of the runs' centres ignores the rows they win.
        var centres = new List<float>();
        for (var y = Math.Max(bandBottom, 0); y <= bandTop; y++)
            if (LongestRunCentre(image, y) is { } runCentre)
                centres.Add(runCentre);
        centres.Sort();
        var side = height * HeadShare;
        var centre = centres.Count > 0 ? centres[centres.Count / 2] : image.Width / 2f;
        return new Square(centre - side / 2f, top + 1 + side * HeadRoom - side, side);
    }

    private static float? LongestRunCentre(PortraitImage image, int y)
    {
        int best = 0,
            bestStart = 0,
            start = -1;
        for (var x = 0; x <= image.Width; x++)
        {
            var ink = x < image.Width && image.Rgba[(y * image.Width + x) * 4 + 3] >= 128;
            if (ink && start < 0)
                start = x;
            if (ink || start < 0)
                continue;
            if (x - start > best)
            {
                best = x - start;
                bestStart = start;
            }
            start = -1;
        }
        return best > 0 ? bestStart + best / 2f : null;
    }

    private static bool RowHasInk(PortraitImage image, int y)
    {
        for (var x = 0; x < image.Width; x++)
            if (image.Rgba[(y * image.Width + x) * 4 + 3] >= 128)
                return true;
        return false;
    }

    /// <summary>
    /// A <paramref name="width"/> by <paramref name="height"/> copy of a part of an image, each
    /// pixel the alpha-weighted mean of the source pixels it covers; outside the image is clear.
    /// Clear pixels take a neighbour's colour, so filtering leaves no dark fringe.
    /// </summary>
    internal static PortraitImage Reduce(
        PortraitImage image,
        float left,
        float bottom,
        float partWidth,
        float partHeight,
        int width,
        int height
    )
    {
        var output = new byte[width * height * 4];
        var stepX = partWidth / width;
        var stepY = partHeight / height;
        for (var oy = 0; oy < height; oy++)
        {
            var y0 = bottom + oy * stepY;
            var y1 = y0 + stepY;
            for (var ox = 0; ox < width; ox++)
            {
                var x0 = left + ox * stepX;
                var x1 = x0 + stepX;
                double r = 0,
                    g = 0,
                    b = 0,
                    a = 0;
                for (var sy = (int)MathF.Floor(y0); sy < y1; sy++)
                {
                    if (sy < 0 || sy >= image.Height)
                        continue;
                    var wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    for (var sx = (int)MathF.Floor(x0); sx < x1; sx++)
                    {
                        if (sx < 0 || sx >= image.Width)
                            continue;
                        var at = (sy * image.Width + sx) * 4;
                        var w = wy * (Math.Min(x1, sx + 1) - Math.Max(x0, sx)) * image.Rgba[at + 3];
                        r += w * image.Rgba[at];
                        g += w * image.Rgba[at + 1];
                        b += w * image.Rgba[at + 2];
                        a += w;
                    }
                }
                var to = (oy * width + ox) * 4;
                if (a <= 0)
                    continue;
                output[to] = (byte)Math.Round(r / a);
                output[to + 1] = (byte)Math.Round(g / a);
                output[to + 2] = (byte)Math.Round(b / a);
                output[to + 3] = (byte)Math.Min(255, Math.Round(a / (stepX * stepY)));
            }
        }
        Bleed(output, width, height);
        return new PortraitImage(width, height, output);
    }

    private static void Bleed(byte[] pixels, int width, int height)
    {
        var source = (byte[])pixels.Clone();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var to = (y * width + x) * 4;
            if (source[to + 3] != 0)
                continue;
            int r = 0,
                g = 0,
                b = 0,
                n = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;
                var at = (ny * width + nx) * 4;
                if (source[at + 3] == 0)
                    continue;
                r += source[at];
                g += source[at + 1];
                b += source[at + 2];
                n++;
            }
            if (n == 0)
                continue;
            pixels[to] = (byte)(r / n);
            pixels[to + 1] = (byte)(g / n);
            pixels[to + 2] = (byte)(b / n);
        }
    }
}
