#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Infrastructure.DesktopKit;

/// <summary>
/// A square of a bust portrait to show in a well, relative to the art's opaque bounds: the
/// centre across the width and down the height (0 at the left and at the top), and the side as
/// a fraction of the height.
/// </summary>
internal readonly record struct KitPortraitFocus(float X, float Y, float Size);

/// <summary>A colour as three bytes, for the pixel work.</summary>
internal readonly record struct KitRgb(byte R, byte G, byte B);

/// <summary>
/// How a hero's medallion is drawn: the face square (or null to find the head), the hero's
/// colour for the glow behind the figure, the dark ground the glow fades to, the rim's colour,
/// and whether the head rises above the plate.
/// </summary>
internal readonly record struct KitMedallion(
    KitPortraitFocus? Focus,
    KitRgb Glow,
    KitRgb Ground,
    KitRgb Rim,
    bool Rise
);

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
    /// the head and not on the whole bust, which a weapon or a shoulder pulls to one side; or
    /// the square <paramref name="focus"/> names. Null when the image has no opaque pixel.
    /// </summary>
    internal static Square? HeadSquare(PortraitImage image, KitPortraitFocus? focus = null)
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
        if (focus is { } chosen)
        {
            var (left, right) = InkColumns(image);
            var chosenSide = height * chosen.Size;
            return new Square(
                left + (right - left + 1) * chosen.X - chosenSide / 2f,
                top + 1 - height * chosen.Y - chosenSide / 2f,
                chosenSide
            );
        }
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

    // A medallion's proportions, as fractions of the plate's side.
    internal const float MedallionRise = .16f;
    private const float MedallionRadius = .14f;
    private const float RimInset = .012f;
    private const float RimWidth = .03f;
    private const float FigureInset = .036f;

    /// <summary>
    /// A hero's medallion: a rounded plate with the hero's colour glowing behind the figure, a
    /// thin rim, and the head and shoulders cut from <paramref name="image"/>. With
    /// <see cref="KitMedallion.Rise"/> the image is taller than the plate by
    /// <see cref="MedallionRise"/> and wider by the same, the plate sits at its bottom centre,
    /// and the head stands above the plate's top edge; the sides and the bottom stay inside the
    /// rim. Null when the image has no opaque pixel.
    /// </summary>
    internal static PortraitImage? Medallion(
        PortraitImage image,
        KitMedallion look,
        int platePixels
    )
    {
        if (HeadSquare(image, look.Focus) is not { } head)
            return null;
        var plate = platePixels;
        var rise = look.Rise ? (int)MathF.Round(plate * MedallionRise) : 0;
        var size = plate + rise;
        // The figure is looser than the face square, and sits lower when it cannot rise.
        var side = head.Side * (look.Rise ? 1.3f : 1.22f);
        var drop = side * (look.Rise ? .02f : .07f);
        var figure = Reduce(
            image,
            head.Left + head.Side / 2f - side / 2f,
            head.Bottom + head.Side / 2f - drop - side / 2f,
            side,
            side,
            size,
            size
        );
        var output = new byte[size * size * 4];
        var plateLeft = rise / 2f;
        float plateTop = rise;
        var radius = plate * MedallionRadius;
        var inset = plate * FigureInset;
        for (var row = 0; row < size; row++)
        for (var x = 0; x < size; x++)
        {
            var px = x + .5f;
            var py = row + .5f;
            var u = (px - plateLeft) / plate;
            var v = (py - plateTop) / plate;
            var distance = Rounded(px - plateLeft, py - plateTop, plate, plate, radius);
            var cover = Clamp01(.5f - distance);

            // The plate: the hero's colour, strongest behind the head, fading to the ground.
            var glow = MathF.Pow(
                Math.Max(
                    0f,
                    1f - MathF.Sqrt((u - .5f) * (u - .5f) + (v - .38f) * (v - .38f)) / .75f
                ),
                1.4f
            );
            var r = look.Ground.R * (1 - glow) + look.Glow.R * .62f * glow;
            var g = look.Ground.G * (1 - glow) + look.Glow.G * .62f * glow;
            var b = look.Ground.B * (1 - glow) + look.Glow.B * .62f * glow;
            var a = cover;

            var rim =
                .92f
                * Clamp01(.5f - (distance + plate * RimInset))
                * Clamp01(.5f + distance + plate * (RimInset + RimWidth));
            var shade =
                .43f
                * Clamp01(.5f - (distance + plate * (RimInset + RimWidth)))
                * Clamp01(.5f + distance + plate * (RimInset + RimWidth + .023f));

            // The figure: inside the rim, and free above the plate when it rises.
            var at = ((size - 1 - row) * size + x) * 4;
            var inside = Clamp01(
                .5f
                    - Rounded(
                        px - plateLeft - inset,
                        py - plateTop - inset,
                        plate - 2 * inset,
                        plate - 2 * inset,
                        radius - inset
                    )
            );
            if (look.Rise && py < plateTop + plate * .3f)
                inside = Math.Max(
                    inside,
                    Clamp01(px - plateLeft - inset + .5f)
                        * Clamp01(plateLeft + plate - inset - px + .5f)
                );
            var fa = figure.Rgba[at + 3] / 255f * inside;
            var fr = figure.Rgba[at];
            var fg = figure.Rgba[at + 1];
            var fb = figure.Rgba[at + 2];
            if (!look.Rise)
            {
                // A figure that stays inside sinks into shadow at the bottom edge.
                var fade = 1f - .59f * Clamp01((v - .72f) / .28f);
                Over(ref r, ref g, ref b, ref a, fr, fg, fb, fa);
                r *= fade;
                g *= fade;
                b *= fade;
                Over(ref r, ref g, ref b, ref a, 0, 0, 0, shade);
                Over(ref r, ref g, ref b, ref a, look.Rim.R, look.Rim.G, look.Rim.B, rim);
            }
            else
            {
                Over(ref r, ref g, ref b, ref a, 0, 0, 0, shade);
                Over(ref r, ref g, ref b, ref a, look.Rim.R, look.Rim.G, look.Rim.B, rim);
                Over(ref r, ref g, ref b, ref a, fr, fg, fb, fa);
            }
            output[at] = (byte)Math.Clamp(MathF.Round(r), 0, 255);
            output[at + 1] = (byte)Math.Clamp(MathF.Round(g), 0, 255);
            output[at + 2] = (byte)Math.Clamp(MathF.Round(b), 0, 255);
            output[at + 3] = (byte)Math.Clamp(MathF.Round(a * 255f), 0, 255);
        }
        Bleed(output, size, size);
        return new PortraitImage(size, size, output);
    }

    // Straight-alpha "over": the top colour at its alpha over what is there.
    private static void Over(
        ref float r,
        ref float g,
        ref float b,
        ref float a,
        float topR,
        float topG,
        float topB,
        float topA
    )
    {
        var alpha = topA + a * (1 - topA);
        if (alpha <= 0)
            return;
        r = (topR * topA + r * a * (1 - topA)) / alpha;
        g = (topG * topA + g * a * (1 - topA)) / alpha;
        b = (topB * topA + b * a * (1 - topA)) / alpha;
        a = alpha;
    }

    // The signed distance from a point to a rounded rectangle at the origin: negative inside.
    private static float Rounded(float x, float y, float width, float height, float radius)
    {
        var dx = MathF.Abs(x - width / 2f) - (width / 2f - radius);
        var dy = MathF.Abs(y - height / 2f) - (height / 2f - radius);
        var outside = MathF.Sqrt(
            Math.Max(dx, 0f) * Math.Max(dx, 0f) + Math.Max(dy, 0f) * Math.Max(dy, 0f)
        );
        return outside + Math.Min(Math.Max(dx, dy), 0f) - radius;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static (int Left, int Right) InkColumns(PortraitImage image)
    {
        int left = image.Width,
            right = -1;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            if (image.Rgba[(y * image.Width + x) * 4 + 3] < 128)
                continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }
        return (left, right);
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
