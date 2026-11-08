#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Game.HeroPortraits;

// A compact admission mask, not captured artwork. Runs contain only the exterior pixels.
internal sealed record PortraitBreakoutPlan(
    PortraitFrameGeometry Frame,
    PortraitFrameGeometry Bounds,
    int Width,
    int Height,
    string[] Parts,
    int[] Runs,
    float ContentScale = 1f,
    float ContentX = 0f,
    float ContentY = 0f
)
{
    internal const int Resolution = 192;
    internal const float SideLimit = 0.4f;
    internal const float TopLimit = 0.65f;
    public PortraitBreakoutRegion[] Regions { get; init; } = [];

    internal static bool WithinEnvelope(PortraitFrameGeometry frame, float x, float y) =>
        x >= frame.Left - frame.Width * SideLimit
        && x <= frame.Right + frame.Width * SideLimit
        && y <= frame.Top + frame.Height * TopLimit;

    internal static PortraitBreakoutPlan Empty(PortraitFrameGeometry frame) =>
        new(frame, frame, 0, 0, [], []);

    internal PortraitComposition Apply(PortraitComposition fit) =>
        fit with
        {
            Scale = fit.Scale * ContentScale,
            X = fit.X * ContentScale + ContentX,
            Y = fit.Y * ContentScale + ContentY,
        };

    internal bool[] Interior()
    {
        var pixels = new bool[Width * Height];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            pixels[y * Width + x] = Frame.Contains(
                Bounds.Left + (x + 0.5f) * Bounds.Width / Width,
                Bounds.Bottom + (y + 0.5f) * Bounds.Height / Height
            );
        return pixels;
    }

    internal bool IsValid(PortraitFrameGeometry frame)
    {
        var centerX = (frame.Left + frame.Right) * 0.5f;
        var centerY = (frame.Bottom + frame.Top) * 0.5f;
        if (
            (float.IsNaN(ContentScale) || float.IsInfinity(ContentScale))
            || ContentScale is < 0.91f or > 1f
            || (float.IsNaN(ContentX) || float.IsInfinity(ContentX))
            || (float.IsNaN(ContentY) || float.IsInfinity(ContentY))
            || Math.Abs(ContentX - centerX * (1 - ContentScale)) > frame.Width * 0.001f
            || Math.Abs(ContentY - centerY * (1 - ContentScale)) > frame.Height * 0.016f
        )
            return false;
        if (Frame != frame || !frame.IsValid || Parts == null || Runs == null)
            return false;
        if (Parts.Length == 0)
            return Width == 0
                && Height == 0
                && Runs.Length == 0
                && Bounds == frame
                && Regions is { Length: 0 };
        if (
            Parts.Length > 512
            || Parts.Any(p => string.IsNullOrEmpty(p) || p.Length > 256)
            || Parts.Distinct(StringComparer.Ordinal).Count() != Parts.Length
            || Width is < 8 or > Resolution
            || Height is < 8 or > Resolution
            || !Bounds.IsValid
            || Bounds.Left < frame.Left - frame.Width * (SideLimit + 0.051f)
            || Bounds.Right > frame.Right + frame.Width * (SideLimit + 0.051f)
            || Bounds.Bottom != frame.Bottom
            || Bounds.Top > frame.Top + frame.Height * (TopLimit + 0.051f)
            || Bounds.Left > frame.Left
            || Bounds.Right < frame.Right
            || Bounds.Top < frame.Top
            || Runs.Length is 0 or > 8192
            || Runs.Length % 2 != 0
        )
            return false;
        var interior = Interior();
        var end = 0;
        for (var i = 0; i < Runs.Length; i += 2)
        {
            var start = Runs[i];
            var length = Runs[i + 1];
            if (start < end || length <= 0 || start > interior.Length - length)
                return false;
            end = start + length;
            for (var j = start; j < end; j++)
                if (
                    interior[j]
                    || !WithinEnvelope(
                        frame,
                        Bounds.Left + (j % Width + 0.5f) * Bounds.Width / Width,
                        Bounds.Bottom + (j / Width + 0.5f) * Bounds.Height / Height
                    )
                )
                    return false;
        }
        return Regions != null
            && Regions.Length is > 0 and <= 512
            && Regions.All(r => r != null && Parts.Contains(r.Part) && r.IsValid())
            && Regions.Select(r => (r.Part, r.Page)).Distinct().Count() == Regions.Length
            && Regions.Sum(r => r.Width * r.Height) <= 2_000_000;
    }
}

// Disjoint UV runs describe material, so clipping follows Spine deformation rather than
// opening a shared screen-space hole for every attachment at that location.
internal sealed record PortraitBreakoutRegion(
    string Part,
    int Page,
    PortraitFrameGeometry Uv,
    int Width,
    int Height,
    int[] Runs
)
{
    public bool BottomCrop { get; init; }

    internal bool Contains(PortraitPoint uv)
    {
        var x = (int)MathF.Floor((uv.X - Uv.Left) * Width / Uv.Width);
        var y = (int)MathF.Floor((uv.Y - Uv.Bottom) * Height / Uv.Height);
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;
        var pixel = y * Width + x;
        var low = 0;
        var high = Runs.Length / 2 - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var start = Runs[middle * 2];
            if (pixel < start)
                high = middle - 1;
            else if (pixel >= start + Runs[middle * 2 + 1])
                low = middle + 1;
            else
                return true;
        }
        return false;
    }

    internal bool IsValid()
    {
        if (
            Page is < 0 or > 3
            || Width is < 2 or > 128
            || Height is < 2 or > 128
            || (float.IsNaN(Uv.Left) || float.IsInfinity(Uv.Left))
            || (float.IsNaN(Uv.Right) || float.IsInfinity(Uv.Right))
            || (float.IsNaN(Uv.Bottom) || float.IsInfinity(Uv.Bottom))
            || (float.IsNaN(Uv.Top) || float.IsInfinity(Uv.Top))
            || Uv.Left < 0
            || Uv.Bottom < 0
            || Uv.Right > 1
            || Uv.Top > 1
            || Uv.Width <= 0
            || Uv.Height <= 0
            || Runs == null
            || Runs.Length is 0 or > 8192
            || Runs.Length % 2 != 0
        )
            return false;
        var end = 0;
        for (var i = 0; i < Runs.Length; i += 2)
        {
            if (Runs[i] < end || Runs[i + 1] <= 0 || Runs[i] > Width * Height - Runs[i + 1])
                return false;
            end = Runs[i] + Runs[i + 1];
        }
        return true;
    }

    internal PortraitFrameGeometry[] Rectangles()
    {
        var rectangles = new List<(int Left, int Bottom, int Right, int Top)>();
        var previous = new Dictionary<(int, int), int>();
        for (var y = 0; y < Height; y++)
        {
            var current = new Dictionary<(int, int), int>();
            for (var x = 0; x < Width; x++)
            {
                if (
                    !Contains(
                        new PortraitPoint(
                            Uv.Left + (x + 0.5f) * Uv.Width / Width,
                            Uv.Bottom + (y + 0.5f) * Uv.Height / Height
                        )
                    )
                )
                    continue;
                var left = x;
                while (
                    x + 1 < Width
                    && Contains(
                        new PortraitPoint(
                            Uv.Left + (x + 1.5f) * Uv.Width / Width,
                            Uv.Bottom + (y + 0.5f) * Uv.Height / Height
                        )
                    )
                )
                    x++;
                var key = (left, x + 1);
                if (previous.TryGetValue(key, out var index))
                {
                    var r = rectangles[index];
                    rectangles[index] = (r.Left, r.Bottom, r.Right, y + 1);
                }
                else
                {
                    index = rectangles.Count;
                    rectangles.Add((left, y, x + 1, y + 1));
                }
                current[key] = index;
            }
            previous = current;
        }
        return rectangles
            .Select(r => new PortraitFrameGeometry(
                Uv.Left + r.Left * Uv.Width / Width,
                Uv.Bottom + r.Bottom * Uv.Height / Height,
                Uv.Left + r.Right * Uv.Width / Width,
                Uv.Bottom + r.Top * Uv.Height / Height
            ))
            .ToArray();
    }
}
