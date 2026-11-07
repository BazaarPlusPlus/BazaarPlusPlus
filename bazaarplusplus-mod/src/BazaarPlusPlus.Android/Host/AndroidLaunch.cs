using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The Android App's Launch Record for this launch (root ADR 0012 and 0013), read once at
/// start and logged. Whatever it says, the host runs normally: a record the host cannot trust is
/// only logged, and no record means the host is not running in the App (a development deploy).
/// The record written for this process names the runtime the App started it with, which
/// self-update compares a release against.
/// </summary>
internal sealed class AndroidLaunch
{
    private AndroidLaunch(LaunchRecordReading reading) => Reading = reading;

    /// <summary>Outside the Android App: no record.</summary>
    public static AndroidLaunch Absent { get; } = new(LaunchRecordReading.Absent);

    public LaunchRecordReading Reading { get; }

    /// <summary>
    /// The non-managed payload digest of the runtime the App started this process with; null
    /// outside the App, for a record of another process, or for a development App.
    /// </summary>
    public string? RuntimeDigest => Reading.Record?.App.NonManagedPayloadDigest;

    /// <summary>
    /// The running game's code as the App measured it before this launch; null outside the App
    /// or for a record of another process.
    /// </summary>
    public AndroidGameCode? Game =>
        Reading.Record?.Game is { } game
            ? new AndroidGameCode(game.VersionCode, game.Il2CppSha256, game.MetadataSha256)
            : null;

    public static AndroidLaunch From(LaunchRecordReading reading) =>
        new(reading ?? throw new ArgumentNullException(nameof(reading)));

    /// <summary>
    /// Reads <c>UserData/BazaarPlusPlus.launch.json</c> for this process and the running Mods'
    /// version, and logs the outcome once.
    /// </summary>
    public static AndroidLaunch Read(string userDataDirectory, string runningVersion)
    {
        AndroidLaunch launch;
        try
        {
            var path = LaunchRecord.PathIn(userDataDirectory);
            launch = From(LaunchRecordReading.Read(path, Environment.ProcessId, runningVersion));
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.launch_record.read_failed"),
                exception
            );
            launch = From(LaunchRecordReading.Untrusted("unreadable"));
        }
        var record = launch.Reading.Record;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.launch_record.read"),
            ("mode", launch.Reading.Mode),
            ("reason", launch.Reading.Reason),
            ("release", record?.Release),
            ("app_version_code", record?.App.VersionCode),
            ("game_version_code", record?.Game.VersionCode),
            ("runtime_digest", launch.RuntimeDigest == null ? "none" : "present")
        );
        return launch;
    }
}
