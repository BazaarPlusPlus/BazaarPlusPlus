#nullable enable

namespace BazaarPlusPlus.GameInterop.Tooltips;

// Native DOTween fades take about 0.15 s of unscaled time. A frame-only timeout can
// expire before the fade finishes at high frame rates.
internal readonly struct NativeTooltipWaitBudget
{
    internal NativeTooltipWaitBudget(int frames, float seconds)
    {
        Frames = frames;
        Seconds = seconds;
    }

    internal int Frames { get; }

    internal float Seconds { get; }

    internal bool IsExpired(int elapsedFrames, float elapsedSeconds) =>
        elapsedFrames >= Frames && elapsedSeconds >= Seconds;
}
