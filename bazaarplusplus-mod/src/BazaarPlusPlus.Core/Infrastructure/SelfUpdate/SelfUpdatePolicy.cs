#nullable enable
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

public enum SelfUpdateDecision
{
    /// <summary>The running copy is the published version or newer.</summary>
    UpToDate,

    /// <summary>Download and stage the published Managed Payload.</summary>
    Stage,

    /// <summary>The published version is staged; the next start swaps it in.</summary>
    Staged,

    /// <summary>Only the installer (on Android, a new Android App) can update this copy.</summary>
    ManualUpdate,
}

/// <summary>
/// Whether the running copy may take the published release as a Managed Payload (ADR-0014). The
/// comparison is against the running copy itself, never against the previous release: a copy
/// that skipped versions still sees every schema or non-managed change between. The release's
/// <c>selfUpdatable</c> flag is a hint for the download page and plays no part here.
/// </summary>
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
