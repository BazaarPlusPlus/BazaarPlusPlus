#nullable enable
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

/// <summary>
/// What the running copy is, for the self-update comparison: its version, the local history
/// database schema it was compiled with, and the non-managed payload digest of the runtime it
/// runs on. That digest is the installed Managed Payload Manifest's, or, inside the Android App,
/// the App's own from its Launch Record, since the installed Mods may have been staged by an
/// earlier App with another runtime (root ADR 0012). A copy without either (a development
/// build) has no digest and never self-updates.
/// </summary>
public sealed record RunningManagedPayload(
    string Version,
    int HistoryDatabaseUserVersion,
    string? NonManagedPayloadDigest
)
{
    /// <summary>The installed manifest's file name beside the mod's assemblies.</summary>
    public const string InstalledManifestFileName = ManagedPayloadHost.InstalledManifestFileName;

    // runtimeDigest: the running runtime's digest when a launcher names it (the Android App's
    // Launch Record); null reads the installed manifest's.
    public static RunningManagedPayload Read(
        string installedManifestPath,
        string version,
        int historyDatabaseUserVersion,
        string? runtimeDigest = null
    )
    {
        if (runtimeDigest != null)
            return new RunningManagedPayload(version, historyDatabaseUserVersion, runtimeDigest);
        string? digest = null;
        try
        {
            if (File.Exists(installedManifestPath))
                digest = ManagedPayloadManifest
                    .Parse(File.ReadAllBytes(installedManifestPath))
                    .NonManagedPayloadDigest;
        }
        catch (Exception exception)
            when (exception is IOException or FormatException or UnauthorizedAccessException)
        {
            digest = null;
        }
        return new RunningManagedPayload(version, historyDatabaseUserVersion, digest);
    }
}
