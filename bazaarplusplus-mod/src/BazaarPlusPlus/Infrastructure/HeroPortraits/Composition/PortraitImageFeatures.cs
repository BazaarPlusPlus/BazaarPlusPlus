#nullable enable

namespace BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

// Numeric image data only: feature extraction runs off the Unity thread. Coordinates refer
// to pixel centers, with the origin at the bottom left, matching the captured texture UVs.
internal sealed record PortraitImage(int Width, int Height, byte[] Rgba);

internal sealed record PortraitFeature(float X, float Y, float[] Descriptor);

internal static class PortraitImageFeatures
{
    internal static List<PortraitFeature> Extract(
        PortraitImage image,
        CancellationToken cancellation
    )
    {
        var gray = new float[image.Width * image.Height];
        var alpha = new float[gray.Length];
        for (var i = 0; i < gray.Length; i++)
        {
            var a = image.Rgba[i * 4 + 3] / 255f;
            alpha[i] = a;
            // Ignore transparent padding and undo PMA for the semi-opaque artwork edges.
            if (a < 0.5f)
                continue;
            gray[i] =
                (
                    0.299f * Math.Min(255f, image.Rgba[i * 4] / a)
                    + 0.587f * Math.Min(255f, image.Rgba[i * 4 + 1] / a)
                    + 0.114f * Math.Min(255f, image.Rgba[i * 4 + 2] / a)
                ) / 255f;
        }
        var result = new List<PortraitFeature>();
        for (var level = 0; level < 8; level++)
        {
            cancellation.ThrowIfCancellationRequested();
            var scale = MathF.Pow(0.8f, level);
            var w = (int)MathF.Round(image.Width * scale);
            var h = (int)MathF.Round(image.Height * scale);
            if (Math.Min(w, h) < 45)
                break;
            var g = Blur(Resize(gray, image.Width, image.Height, w, h), w, h, 1f);
            var a = Resize(alpha, image.Width, image.Height, w, h);
            var gx = new float[g.Length];
            var gy = new float[g.Length];
            var xx = new float[g.Length];
            var yy = new float[g.Length];
            var xy = new float[g.Length];
            for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                var i = y * w + x;
                var dx =
                    (
                        g[i - w + 1]
                        + 2 * g[i + 1]
                        + g[i + w + 1]
                        - g[i - w - 1]
                        - 2 * g[i - 1]
                        - g[i + w - 1]
                    ) * 0.125f;
                var dy =
                    (
                        g[i + w - 1]
                        + 2 * g[i + w]
                        + g[i + w + 1]
                        - g[i - w - 1]
                        - 2 * g[i - w]
                        - g[i - w + 1]
                    ) * 0.125f;
                gx[i] = dx;
                gy[i] = dy;
                xx[i] = dx * dx;
                yy[i] = dy * dy;
                xy[i] = dx * dy;
            }
            xx = Blur(xx, w, h, 1.2f);
            yy = Blur(yy, w, h, 1.2f);
            xy = Blur(xy, w, h, 1.2f);
            var response = new float[g.Length];
            var maximum = 0f;
            for (var y = 12; y < h - 12; y++)
            for (var x = 12; x < w - 12; x++)
            {
                var i = y * w + x;
                var trace = xx[i] + yy[i];
                response[i] = xx[i] * yy[i] - xy[i] * xy[i] - 0.04f * trace * trace;
                maximum = Math.Max(maximum, response[i]);
            }
            var corners = new List<(int X, int Y, float Score)>();
            for (var y = 12; y < h - 12; y++)
            for (var x = 12; x < w - 12; x++)
            {
                var i = y * w + x;
                var score = response[i];
                if (a[i] < 0.86f || score <= maximum * 0.007f || score <= 0f)
                    continue;
                var peak = true;
                for (var dy = -2; dy <= 2 && peak; dy++)
                for (var dx = -2; dx <= 2; dx++)
                    if (response[(y + dy) * w + x + dx] > score)
                    {
                        peak = false;
                        break;
                    }
                if (peak)
                    corners.Add((x, y, score));
            }
            corners.Sort((left, right) => right.Score.CompareTo(left.Score));
            foreach (var corner in corners.Take(150))
            {
                var hist = new float[36];
                for (var dy = -7; dy <= 7; dy++)
                for (var dx = -7; dx <= 7; dx++)
                {
                    var i = (corner.Y + dy) * w + corner.X + dx;
                    var angle = MathF.Atan2(gy[i], gx[i]) + MathF.PI;
                    hist[(int)(angle * 36f / (2 * MathF.PI)) % 36] +=
                        MathF.Sqrt(gx[i] * gx[i] + gy[i] * gy[i])
                        * MathF.Exp(-(dx * dx + dy * dy) / 32f);
                }
                var smooth = new float[36];
                for (var i = 0; i < 36; i++)
                    smooth[i] = (hist[(i + 35) % 36] + hist[i] + hist[(i + 1) % 36]) / 3f;
                var limit = smooth.Max() * 0.8f;
                var orientations = Enumerable
                    .Range(0, 36)
                    .Where(i =>
                        smooth[i] >= limit
                        && smooth[i] >= smooth[(i + 35) % 36]
                        && smooth[i] > smooth[(i + 1) % 36]
                    )
                    .OrderByDescending(i => smooth[i])
                    .Take(2);
                foreach (var orientation in orientations)
                {
                    var theta = (orientation + 0.5f) * 2 * MathF.PI / 36 - MathF.PI;
                    var c = MathF.Cos(theta);
                    var s = MathF.Sin(theta);
                    var descriptor = new float[128];
                    for (var dy = -8; dy < 8; dy++)
                    for (var dx = -8; dx < 8; dx++)
                    {
                        var px = (int)MathF.Round(corner.X + c * dx - s * dy);
                        var py = (int)MathF.Round(corner.Y + s * dx + c * dy);
                        var i = py * w + px;
                        var angle =
                            (MathF.Atan2(gy[i], gx[i]) - theta + 4 * MathF.PI) % (2 * MathF.PI);
                        var bin = angle * 8 / (2 * MathF.PI);
                        var b = (int)bin;
                        var value =
                            MathF.Sqrt(gx[i] * gx[i] + gy[i] * gy[i])
                            * MathF.Exp(-(dx * dx + dy * dy) / 128f);
                        var cell = (((dy + 8) / 4) * 4 + (dx + 8) / 4) * 8;
                        descriptor[cell + b % 8] += value * (1 - (bin - b));
                        descriptor[cell + (b + 1) % 8] += value * (bin - b);
                    }
                    Normalize(descriptor);
                    for (var i = 0; i < descriptor.Length; i++)
                        descriptor[i] = Math.Min(0.2f, descriptor[i]);
                    Normalize(descriptor);
                    result.Add(
                        new PortraitFeature(
                            (corner.X + 0.5f) * image.Width / w,
                            (corner.Y + 0.5f) * image.Height / h,
                            descriptor
                        )
                    );
                }
            }
        }
        return result;
    }

    private static void Normalize(float[] values)
    {
        var norm = 1e-8f;
        foreach (var value in values)
            norm += value * value;
        norm = MathF.Sqrt(norm);
        for (var i = 0; i < values.Length; i++)
            values[i] /= norm;
    }

    private static float[] Resize(float[] source, int width, int height, int w, int h)
    {
        var result = new float[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var fx = Math.Max(0, (x + 0.5f) * width / w - 0.5f);
            var fy = Math.Max(0, (y + 0.5f) * height / h - 0.5f);
            var ix = Math.Min(width - 2, (int)fx);
            var iy = Math.Min(height - 2, (int)fy);
            var dx = fx - ix;
            var dy = fy - iy;
            result[y * w + x] =
                (source[iy * width + ix] * (1 - dx) + source[iy * width + ix + 1] * dx) * (1 - dy)
                + (
                    source[(iy + 1) * width + ix] * (1 - dx)
                    + source[(iy + 1) * width + ix + 1] * dx
                ) * dy;
        }
        return result;
    }

    private static float[] Blur(float[] source, int w, int h, float sigma)
    {
        var radius = (int)MathF.Ceiling(sigma * 3);
        var kernel = new float[radius * 2 + 1];
        var sum = 0f;
        for (var i = -radius; i <= radius; i++)
        {
            kernel[i + radius] = MathF.Exp(-i * i / (2 * sigma * sigma));
            sum += kernel[i + radius];
        }
        for (var i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;
        var temp = new float[source.Length];
        var result = new float[source.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var k = -radius; k <= radius; k++)
            temp[y * w + x] +=
                source[y * w + Math.Max(0, Math.Min(w - 1, x + k))] * kernel[k + radius];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var k = -radius; k <= radius; k++)
            result[y * w + x] +=
                temp[Math.Max(0, Math.Min(h - 1, y + k)) * w + x] * kernel[k + radius];
        return result;
    }
}
