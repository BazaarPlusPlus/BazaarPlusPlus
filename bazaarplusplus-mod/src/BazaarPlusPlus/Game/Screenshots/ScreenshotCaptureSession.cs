#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotCaptureSession
{
    public ScreenshotCaptureSession(Task frameAcquired, Task<ScreenshotCaptureResult?> completion)
    {
        FrameAcquired = frameAcquired ?? throw new ArgumentNullException(nameof(frameAcquired));
        Completion = completion ?? throw new ArgumentNullException(nameof(completion));
    }

    public Task FrameAcquired { get; }

    public Task<ScreenshotCaptureResult?> Completion { get; }
}
