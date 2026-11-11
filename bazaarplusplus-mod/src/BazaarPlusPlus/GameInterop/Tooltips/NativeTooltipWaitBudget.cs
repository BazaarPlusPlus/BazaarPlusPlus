#nullable enable

namespace BazaarPlusPlus.GameInterop.Tooltips;

/// <summary>
/// Bounds a wait for native tooltip work in both frames and unscaled seconds.
/// </summary>
/// <remarks>
/// Native tooltip shows and hides are DOTween sequences on unscaled time, so a fade-in takes
/// about 0.15 s at any frame rate. A frame count alone is a time bound that shrinks as the frame
/// rate rises: 60 frames is 1 s at 60 fps but 0.125 s at 480 fps, which ends the wait before the
/// fade-in does. A wait therefore expires only when both floors have passed. It never ends sooner
/// in time than its seconds floor, nor in fewer frames than its frame floor.
/// </remarks>
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
