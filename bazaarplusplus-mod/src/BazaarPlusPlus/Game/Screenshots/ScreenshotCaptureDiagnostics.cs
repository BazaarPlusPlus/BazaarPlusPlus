#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class ScreenshotCaptureDiagnostics
{
    internal static void ReportCleanupFailed(
        ScreenshotCaptureCleanupStage stage,
        string? screenshotId,
        string? filePath,
        Exception exception
    )
    {
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.Screenshots, "screenshots.capture.cleanup_failed"),
            exception,
            () =>
                [
                    ("stage", stage),
                    ("screenshot_id", screenshotId, BppLogCorrelationPolicy.Short),
                    ("file_path", filePath),
                ]
        );
    }
}
