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
    internal const int MaxParts = 24;
    internal const int Resolution = 192;

    // A thin moving object sweeps a larger envelope than it displays in any one pose.
    internal const float AreaBudget = 0.36f;
    internal const float PoseAreaBudget = 0.10f;
    internal const float SideLimit = 0.4f;
    internal const float TopLimit = 0.65f;

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
            return Width == 0 && Height == 0 && Runs.Length == 0 && Bounds == frame;
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
        var budget = (int)(interior.Count(p => p) * AreaBudget);
        var end = 0;
        var count = 0;
        for (var i = 0; i < Runs.Length; i += 2)
        {
            var start = Runs[i];
            var length = Runs[i + 1];
            if (start < end || length <= 0 || start > interior.Length - length)
                return false;
            end = start + length;
            count += length;
            if (count > budget)
                return false;
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
        return true;
    }
}
