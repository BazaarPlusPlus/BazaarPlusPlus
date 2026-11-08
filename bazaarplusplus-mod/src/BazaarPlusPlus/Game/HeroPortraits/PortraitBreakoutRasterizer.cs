#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed record PortraitBreakoutRaster(byte[] Pixels, int[] Owners, PortraitPoint[] Uvs);

internal static class PortraitBreakoutRasterizer
{
    internal static PortraitBreakoutRaster? Render(
        PortraitImage[] pages,
        PortraitPose pose,
        PortraitComposition fit,
        PortraitBreakoutPlan plan,
        Dictionary<string, int> ids,
        CancellationToken cancellation
    )
    {
        if (
            pages.Any(p =>
                p.Width < 2 || p.Height < 2 || (long)p.Width * p.Height * 4 != p.Rgba.Length
            )
        )
            return null;
        var width = plan.Width;
        var height = plan.Height;
        var pixels = new byte[width * height * 4];
        var owners = Enumerable.Repeat(-1, width * height).ToArray();
        var uvs = new PortraitPoint[width * height];
        var visits = 0L;
        foreach (var triangle in pose.Triangles)
        {
            cancellation.ThrowIfCancellationRequested();
            if (triangle.Page < 0 || triangle.Page >= pages.Length)
                return null;
            var a = Project(triangle.A, fit, plan);
            var b = Project(triangle.B, fit, plan);
            var c = Project(triangle.C, fit, plan);
            if (!Finite(a) || !Finite(b) || !Finite(c))
                return null;
            var area = Edge(a, b, c);
            if (Math.Abs(area) < 0.00001f)
                continue;
            var partId =
                triangle.Part != null && ids.TryGetValue(triangle.Part, out var found) ? found : -1;
            var left = (int)
                Math.Max(0, Math.Min(width - 1, Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))));
            var right = (int)
                Math.Min(width - 1, Math.Max(-1, Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)))));
            var bottom = (int)
                Math.Max(0, Math.Min(height - 1, Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))));
            var top = (int)
                Math.Min(height - 1, Math.Max(-1, Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)))));
            if (right < left || top < bottom)
                continue;
            visits += (long)(right - left + 1) * (top - bottom + 1);
            if (visits > 4_000_000)
                return null;
            var page = pages[triangle.Page];
            var owner = triangle.BreakoutEligible && partId >= 0 ? partId : -2;
            for (var y = bottom; y <= top; y++)
            for (var x = left; x <= right; x++)
            {
                var p = new PortraitPoint(x + 0.5f, y + 0.5f);
                var wa = Edge(b, c, p) / area;
                var wb = Edge(c, a, p) / area;
                var wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0)
                    continue;
                var u = wa * triangle.UvA.X + wb * triangle.UvB.X + wc * triangle.UvC.X;
                var v = wa * triangle.UvA.Y + wb * triangle.UvB.Y + wc * triangle.UvC.Y;
                if (float.IsNaN(u) || float.IsNaN(v) || float.IsInfinity(u) || float.IsInfinity(v))
                    return null;
                var tx = Math.Max(0, Math.Min(page.Width - 1, (int)(u * page.Width)));
                var ty = Math.Max(0, Math.Min(page.Height - 1, (int)(v * page.Height)));
                var source = (ty * page.Width + tx) * 4;
                var alpha = page.Rgba[source + 3] * triangle.Opacity;
                if (alpha < 8)
                    continue;
                var index = y * width + x;
                var output = index * 4;
                var inverse = 1 - alpha / 255f;
                for (var channel = 0; channel < 3; channel++)
                    pixels[output + channel] = (byte)
                        Math.Min(
                            255,
                            page.Rgba[source + channel] * triangle.Opacity
                                + pixels[output + channel] * inverse
                        );
                pixels[output + 3] = (byte)Math.Min(255, alpha + pixels[output + 3] * inverse);
                // A translucent or specially blended overlay invalidates evidence for the
                // surface underneath. Last nonzero-alpha is not reliable ownership.
                owners[index] = alpha >= 240 ? owner : -2;
                uvs[index] = new PortraitPoint(u, v);
            }
        }
        return new PortraitBreakoutRaster(pixels, owners, uvs);
    }

    private static PortraitPoint Project(
        PortraitPoint p,
        PortraitComposition fit,
        PortraitBreakoutPlan plan
    ) =>
        new(
            (p.X * fit.Scale + fit.X - plan.Bounds.Left) * plan.Width / plan.Bounds.Width,
            (p.Y * fit.Scale + fit.Y - plan.Bounds.Bottom) * plan.Height / plan.Bounds.Height
        );

    private static bool Finite(PortraitPoint p) =>
        !float.IsNaN(p.X) && !float.IsInfinity(p.X) && !float.IsNaN(p.Y) && !float.IsInfinity(p.Y);

    private static float Edge(PortraitPoint a, PortraitPoint b, PortraitPoint p) =>
        (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
}
