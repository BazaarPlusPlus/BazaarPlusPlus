#nullable enable
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

public enum SelfUpdateDecision
{
    UpToDate,

    Stage,

    Staged,

    ManualUpdate,
}

// Compare against the running copy so skipped releases cannot hide schema or runtime changes.
// The release's selfUpdatable hint is for the download page.
public static class SelfUpdatePolicy
{
    public static SelfUpdateDecision Decide(
        RunningManagedPayload running,
        string latestVersion,
        ManagedPayloadRelease? release,
        bool staged
    )
    {
        if (running == null)
            throw new ArgumentNullException(nameof(running));
        if (
            !ManagedPayloadVersion.TryParse(running.Version, out var current)
            || !ManagedPayloadVersion.TryParse(latestVersion, out var latest)
            || current >= latest
        )
            return SelfUpdateDecision.UpToDate;

        if (
            release == null
            || release.Version != latestVersion
            || running.NonManagedPayloadDigest == null
            || release.HistoryDatabaseUserVersion != running.HistoryDatabaseUserVersion
            || !string.Equals(
                release.NonManagedPayloadDigest,
                running.NonManagedPayloadDigest,
                StringComparison.Ordinal
            )
        )
            return SelfUpdateDecision.ManualUpdate;

        return staged ? SelfUpdateDecision.Staged : SelfUpdateDecision.Stage;
    }
}
