#nullable enable
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

/// <summary>
/// What the running copy is, for the self-update comparison: its version, the local history
/// database schema it was compiled with, and the non-managed payload digest its installed
/// Managed Payload Manifest records. A copy without that manifest (a development build) has no
/// digest and never self-updates.
/// </summary>
public sealed record RunningManagedPayload(
    string Version,
    int HistoryDatabaseUserVersion,
    string? NonManagedPayloadDigest
)
{
    /// <summary>The installed manifest's file name beside the mod's assemblies.</summary>
    public const string InstalledManifestFileName = ManagedPayloadHost.InstalledManifestFileName;

    public static RunningManagedPayload Read(
        string installedManifestPath,
        string version,
        int historyDatabaseUserVersion
    )
    {
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
