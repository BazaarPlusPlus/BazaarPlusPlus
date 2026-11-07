#nullable enable

namespace BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

internal sealed record PortraitPoseImage(
    PortraitImage Image,
    PortraitPoint Origin,
    float PixelsPerUnit
)
{
    internal PortraitPoint ToCharacter(PortraitPoint uv) =>
        new(
            Origin.X + uv.X * Image.Width / PixelsPerUnit,
            Origin.Y + uv.Y * Image.Height / PixelsPerUnit
        );
}

// A bounded numeric matching proxy, not a replacement for Spine's native renderer.
// Pixels and geometry both use a bottom-left origin; output retains premultiplied alpha.
internal static class PortraitPoseRasterizer
{
    private const int MaxTriangles = 10_000;
    private const int MaxPixelVisits = 8_000_000;

    internal static PortraitPoseImage? Render(
        PortraitImage[] pages,
        PortraitPose pose,
        CancellationToken cancellation,
        int maxDimension = 640
    )
    {
        cancellation.ThrowIfCancellationRequested();
        if (pose.Triangles.Length is 0 or > MaxTriangles || maxDimension is < 8 or > 640)
            return null;
        foreach (var page in pages)
            if (
                page.Width < 2
                || page.Height < 2
                || (long)page.Width * page.Height * 4 != page.Rgba.Length
            )
                return null;
        var lowX = float.PositiveInfinity;
        var lowY = lowX;
        var highX = float.NegativeInfinity;
        var highY = highX;
        foreach (var t in pose.Triangles)
        {
            cancellation.ThrowIfCancellationRequested();
            if (
                t.Page < 0
                || t.Page >= pages.Length
                || !Finite(t.A)
                || !Finite(t.B)
                || !Finite(t.C)
                || !ValidUv(t.UvA)
                || !ValidUv(t.UvB)
                || !ValidUv(t.UvC)
            )
                return null;
            lowX = Math.Min(lowX, Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)));
            lowY = Math.Min(lowY, Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)));
            highX = Math.Max(highX, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
            highY = Math.Max(highY, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        }
        var extentX = highX - lowX;
        var extentY = highY - lowY;
        var extent = Math.Max(extentX, extentY);
        if (!Finite(extent) || extent <= 0 || extentX <= 0 || extentY <= 0)
            return null;
        var scale = (maxDimension - 4f) / extent;
        if (!Finite(scale) || scale <= 0)
            return null;
        var width = Math.Min(maxDimension, Math.Max(2, (int)MathF.Ceiling(extentX * scale) + 4));
        var height = Math.Min(maxDimension, Math.Max(2, (int)MathF.Ceiling(extentY * scale) + 4));
        var origin = new PortraitPoint(lowX - 2f / scale, lowY - 2f / scale);
        if (!Finite(origin))
            return null;
        var pixels = new byte[width * height * 4];
        var visits = 0L;
        foreach (var t in pose.Triangles)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = ToPixel(t.A, origin, scale);
            var b = ToPixel(t.B, origin, scale);
            var c = ToPixel(t.C, origin, scale);
            if (!Finite(a) || !Finite(b) || !Finite(c))
                return null;
            var uvB = t.UvB;
            var uvC = t.UvC;
            var area = Edge(a, b, c);
            if (!Finite(area))
                return null;
            if (MathF.Abs(area) < 0.000001f)
                continue;
            if (area < 0)
            {
                (b, c) = (c, b);
                (uvB, uvC) = (uvC, uvB);
                area = -area;
            }
            var left = Math.Max(0, (int)MathF.Ceiling(Math.Min(a.X, Math.Min(b.X, c.X)) - 0.5f));
            var right = Math.Min(
                width - 1,
                (int)MathF.Floor(Math.Max(a.X, Math.Max(b.X, c.X)) - 0.5f)
            );
            var bottom = Math.Max(0, (int)MathF.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 0.5f));
            var top = Math.Min(
                height - 1,
                (int)MathF.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - 0.5f)
            );
            if (right < left || top < bottom)
                continue;
            visits += (long)(right - left + 1) * (top - bottom + 1);
            if (visits > MaxPixelVisits)
                return null;
            var page = pages[t.Page];
            var edgeA = Inclusive(b, c);
            var edgeB = Inclusive(c, a);
            var edgeC = Inclusive(a, b);
            for (var y = bottom; y <= top; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = left; x <= right; x++)
                {
                    var point = new PortraitPoint(x + 0.5f, y + 0.5f);
                    var wa = Edge(b, c, point);
                    var wb = Edge(c, a, point);
                    var wc = Edge(a, b, point);
                    if (!Covered(wa, edgeA) || !Covered(wb, edgeB) || !Covered(wc, edgeC))
                        continue;
                    wa /= area;
                    wb /= area;
                    wc /= area;
                    var u = wa * t.UvA.X + wb * uvB.X + wc * uvC.X;
                    var v = wa * t.UvA.Y + wb * uvB.Y + wc * uvC.Y;
                    Composite(page, u, v, pixels, (y * width + x) * 4);
                }
            }
        }
        return new PortraitPoseImage(new PortraitImage(width, height, pixels), origin, scale);
    }

    private static PortraitPoint ToPixel(PortraitPoint p, PortraitPoint origin, float scale) =>
        new((p.X - origin.X) * scale, (p.Y - origin.Y) * scale);

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool Finite(PortraitPoint p) => Finite(p.X) && Finite(p.Y);

    private static bool ValidUv(PortraitPoint p) =>
        Finite(p) && p.X is >= -0.001f and <= 1.001f && p.Y is >= -0.001f and <= 1.001f;

    private static float Edge(PortraitPoint a, PortraitPoint b, PortraitPoint p) =>
        (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    // Shared edges belong to one triangle only, including after a skeleton flip.
    private static bool Inclusive(PortraitPoint a, PortraitPoint b) =>
        b.Y < a.Y || (b.Y == a.Y && b.X > a.X);

    private static bool Covered(float edge, bool inclusive) => edge > 0 || (edge == 0 && inclusive);

    private static void Composite(PortraitImage page, float u, float v, byte[] pixels, int output)
    {
        var x = Math.Max(0, Math.Min(page.Width - 1f, u * page.Width - 0.5f));
        var y = Math.Max(0, Math.Min(page.Height - 1f, v * page.Height - 0.5f));
        var ix = Math.Min(page.Width - 2, (int)x);
        var iy = Math.Min(page.Height - 2, (int)y);
        var dx = x - ix;
        var dy = y - iy;
        var a = (iy * page.Width + ix) * 4;
        var b = a + 4;
        var c = a + page.Width * 4;
        var d = c + 4;
        var alpha = Sample(page.Rgba, a + 3, b + 3, c + 3, d + 3, dx, dy);
        if (alpha <= 0)
            return;
        var inverse = 1f - alpha / 255f;
        for (var channel = 0; channel < 3; channel++)
        {
            var value = Sample(
                page.Rgba,
                a + channel,
                b + channel,
                c + channel,
                d + channel,
                dx,
                dy
            );
            pixels[output + channel] = (byte)
                Math.Min(255f, value + pixels[output + channel] * inverse + 0.5f);
        }
        pixels[output + 3] = (byte)Math.Min(255f, alpha + pixels[output + 3] * inverse + 0.5f);
    }

    private static float Sample(byte[] bytes, int a, int b, int c, int d, float x, float y) =>
        (bytes[a] * (1 - x) + bytes[b] * x) * (1 - y) + (bytes[c] * (1 - x) + bytes[d] * x) * y;
}
