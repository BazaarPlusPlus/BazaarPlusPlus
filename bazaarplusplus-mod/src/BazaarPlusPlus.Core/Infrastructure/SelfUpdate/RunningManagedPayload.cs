#nullable enable
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

// Prefer the Android App's Launch Record digest because an earlier App may have staged the installed Mods.
public sealed record RunningManagedPayload(
    string Version,
    int HistoryDatabaseUserVersion,
    string? NonManagedPayloadDigest
)
{
    public const string InstalledManifestFileName = ManagedPayloadHost.InstalledManifestFileName;

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
