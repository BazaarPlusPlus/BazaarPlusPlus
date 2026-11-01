using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// What this launch lets the host do, from the Android App's Launch Record (root ADR 0012),
/// read once at start: in Compatibility Mode, or on any record the host cannot trust, nothing
/// leaves the device (no Bundle upload pump, no Ghost sync) and every Run is History-Only, so no
/// later launch seals it either, while local recording and History run as usual; the main-menu
/// label and the settings section say so. No record means
/// the host is not running in the App (a development deploy) and runs normally. The record
/// written for this process also names the runtime the App started it with, which self-update
/// compares a release against.
/// </summary>
internal sealed class AndroidLaunch
{
    private AndroidLaunch(LaunchRecordReading reading) => Reading = reading;

    /// <summary>Outside the Android App: no record.</summary>
    public static AndroidLaunch Absent { get; } = new(LaunchRecordReading.Absent);

    public LaunchRecordReading Reading { get; }

    /// <summary>Compatibility Mode: the record says so, or cannot be trusted.</summary>
    public bool CompatibilityMode => Reading.Mode == LaunchRecordMode.Compatibility;

    /// <summary>Whether Bundles upload and History syncs Ghost battles.</summary>
    public bool Uploads => !CompatibilityMode;

    /// <summary>
    /// The policy of the Runs this launch records: in Compatibility Mode History-Only, so no Run
    /// recorded on an interop nobody vouched for is sealed, now or by a later launch.
    /// </summary>
    public RunUploadPolicy RunUploadPolicy =>
        CompatibilityMode ? RunUploadPolicy.HistoryOnly : RunUploadPolicy.Bundle;

    /// <summary>
    /// The non-managed payload digest of the runtime the App started this process with; null
    /// outside the App, for a record of another process, or for a development App.
    /// </summary>
    public string? RuntimeDigest => Reading.Record?.App.NonManagedPayloadDigest;

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
            // Rather upload nothing than upload on an interop nobody vouched for.
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
            ("uploads", launch.Uploads),
            ("release", record?.Release),
            ("app_version_code", record?.App.VersionCode),
            ("game_version_code", record?.Game.VersionCode),
            ("interop_source", record?.Interop.Source),
            ("runtime_digest", launch.RuntimeDigest == null ? "none" : "present")
        );
        return launch;
    }
}
