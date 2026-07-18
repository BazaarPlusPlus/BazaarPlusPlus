#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayVideoCaptureDefaults
{
    internal const int FallbackFps = 30;
    internal const int MaxFps = 60;
    internal const int Crf = 23;
    internal const string Preset = "veryfast";

    internal static bool TryGetEvenDimensions(
        int sourceWidth,
        int sourceHeight,
        out int width,
        out int height
    )
    {
        width = sourceWidth & ~1;
        height = sourceHeight & ~1;
        return width > 0 && height > 0;
    }
}
