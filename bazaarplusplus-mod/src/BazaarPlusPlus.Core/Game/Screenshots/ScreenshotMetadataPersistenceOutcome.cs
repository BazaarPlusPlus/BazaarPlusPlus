#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

public enum ScreenshotMetadataPersistenceStatus
{
    Saved,
    Unavailable,
    Failed,
    TimedOut,
}

public readonly record struct ScreenshotMetadataPersistenceOutcome(
    ScreenshotMetadataPersistenceStatus Status,
    Exception? Exception
)
{
    public static ScreenshotMetadataPersistenceOutcome Saved() =>
        new(ScreenshotMetadataPersistenceStatus.Saved, null);

    public static ScreenshotMetadataPersistenceOutcome Unavailable() =>
        new(ScreenshotMetadataPersistenceStatus.Unavailable, null);

    public static ScreenshotMetadataPersistenceOutcome Failed(Exception exception) =>
        new(ScreenshotMetadataPersistenceStatus.Failed, exception);

    public static ScreenshotMetadataPersistenceOutcome TimedOut() =>
        new(ScreenshotMetadataPersistenceStatus.TimedOut, null);
}
