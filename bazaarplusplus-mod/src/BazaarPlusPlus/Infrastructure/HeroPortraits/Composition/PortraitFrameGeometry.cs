#nullable enable

namespace BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

// Coordinates are reference-image pixels divided by image height, with a bottom-left origin.
// This is the same 24-segment arch used by the native portrait mask.
internal readonly record struct PortraitFrameGeometry(
    float Left,
    float Bottom,
    float Right,
    float Top
)
{
    internal const int ArchSegments = 24;
    internal float Width => Right - Left;
    internal float Height => Top - Bottom;
    internal float Bevel => Math.Min(Width, Height) * 0.007f;

    internal float Roof(float x)
    {
        var segment = Math.Max(0f, Math.Min(ArchSegments, (x - Left) / Width * ArchSegments));
        var index = Math.Min(ArchSegments - 1, (int)segment);
        var a = index * 2f / ArchSegments - 1f;
        var b = (index + 1) * 2f / ArchSegments - 1f;
        var t = segment - index;
        return Top - Height * 0.15f * (a * a * (1 - t) + b * b * t);
    }

    internal bool Contains(float x, float y) =>
        x >= Left
        && x <= Right
        && y >= Bottom
        && y <= Roof(x)
        && x - Left + y - Bottom >= Bevel
        && Right - x + y - Bottom >= Bevel;

    internal bool IsValid =>
        Finite(Left)
        && Finite(Bottom)
        && Finite(Right)
        && Finite(Top)
        && Width is > 0.01f and < 10f
        && Height is > 0.01f and < 10f;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
