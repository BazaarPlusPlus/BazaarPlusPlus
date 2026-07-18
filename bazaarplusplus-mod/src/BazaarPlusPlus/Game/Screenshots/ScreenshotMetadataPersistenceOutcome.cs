#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

internal enum ScreenshotMetadataPersistenceStatus
{
    Saved,
    Unavailable,
    Failed,
    TimedOut,
}

internal readonly record struct ScreenshotMetadataPersistenceOutcome(
    ScreenshotMetadataPersistenceStatus Status,
    Exception? Exception
)
{
    internal static ScreenshotMetadataPersistenceOutcome Saved() =>
        new(ScreenshotMetadataPersistenceStatus.Saved, null);

    internal static ScreenshotMetadataPersistenceOutcome Unavailable() =>
        new(ScreenshotMetadataPersistenceStatus.Unavailable, null);

    internal static ScreenshotMetadataPersistenceOutcome Failed(Exception exception) =>
        new(ScreenshotMetadataPersistenceStatus.Failed, exception);

    internal static ScreenshotMetadataPersistenceOutcome TimedOut() =>
        new(ScreenshotMetadataPersistenceStatus.TimedOut, null);
}
