using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Android.Host;

// A missing or untrusted Launch Record leaves the host operational (root ADR 0013).
internal sealed class AndroidLaunch
{
    private AndroidLaunch(LaunchRecordReading reading) => Reading = reading;

    public static AndroidLaunch Absent { get; } = new(LaunchRecordReading.Absent);

    public LaunchRecordReading Reading { get; }

    public string? RuntimeDigest => Reading.Record?.App.NonManagedPayloadDigest;

    public AndroidGameCode? Game =>
        Reading.Record?.Game is { } game
            ? new AndroidGameCode(game.VersionCode, game.Il2CppSha256, game.MetadataSha256)
            : null;

    public static AndroidLaunch From(LaunchRecordReading reading) =>
        new(reading ?? throw new ArgumentNullException(nameof(reading)));

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
