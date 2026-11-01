#nullable enable
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.Storage.RunLog;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// What the compiled host makes of the Android App's Launch Record (root ADR 0012), over the
/// App's golden: whether anything leaves the device, and which runtime self-update compares a
/// release with. The record chain itself calls into IL2CPP, so the composition reads its upload
/// switch from <see cref="AndroidLaunch"/>; History's composition is checked in
/// <see cref="AndroidHistoryCompositionTests"/>.
/// </summary>
public sealed class AndroidLaunchTests : IDisposable
{
    internal const int GoldenProcessId = 12345;
    internal const string GoldenRelease = "6.1.0";
    private const string GoldenDigest =
        "b022c26b660889ad3a1026a358432a2857ae3f9f5e5495f04eb21afc14242c09";

    private readonly string _root = Directory
        .CreateTempSubdirectory("bpp-android-launch-")
        .FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    internal static string Golden(Action<JsonObject>? mutate = null)
    {
        using var stream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("AndroidHost.Tests.launch-record.v1.json")
            ?? throw new InvalidOperationException("The Launch Record golden is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        if (mutate == null)
            return text;
        var record = JsonNode.Parse(text)!.AsObject();
        mutate(record);
        return record.ToJsonString();
    }

    internal static AndroidLaunch Launch(string text, string running = GoldenRelease) =>
        AndroidLaunch.From(LaunchRecordReading.Classify(text, GoldenProcessId, running));

    [Fact]
    public void A_normal_record_uploads_and_names_the_running_runtime()
    {
        var launch = Launch(Golden());

        Assert.False(launch.CompatibilityMode);
        Assert.True(launch.Uploads);
        Assert.Equal(GoldenDigest, launch.RuntimeDigest);
    }

    [Fact]
    public void No_record_is_a_development_deploy_that_uploads()
    {
        var launch = AndroidLaunch.Read(_root, GoldenRelease);

        Assert.Equal(LaunchRecordMode.Absent, launch.Reading.Mode);
        Assert.False(launch.CompatibilityMode);
        Assert.True(launch.Uploads);
        Assert.Null(launch.RuntimeDigest);
    }

    [Fact]
    public void The_host_reads_its_own_process_s_record_from_user_data()
    {
        File.WriteAllText(
            Path.Combine(_root, "BazaarPlusPlus.launch.json"),
            Golden(record => record["processId"] = Environment.ProcessId)
        );

        var launch = AndroidLaunch.Read(_root, GoldenRelease);

        Assert.Equal(LaunchRecordMode.Normal, launch.Reading.Mode);
        Assert.True(launch.Uploads);
        Assert.Equal(GoldenDigest, launch.RuntimeDigest);
    }

    public static TheoryData<string, string, string> CompatibilityRecords =>
        new()
        {
            {
                "mode_compatibility",
                Golden(record => record["mode"] = "compatibility"),
                GoldenRelease
            },
            { "process_mismatch", Golden(record => record["processId"] = 54321), GoldenRelease },
            { "release_mismatch", Golden(), "6.0.2" },
            { "schema_unknown", Golden(record => record["schemaVersion"] = 2), GoldenRelease },
            { "mode_unknown", Golden(record => record["mode"] = "fast"), GoldenRelease },
            { "invalid_json", "{", GoldenRelease },
        };

    [Theory]
    [MemberData(nameof(CompatibilityRecords))]
    public void Compatibility_mode_and_untrusted_records_upload_nothing(
        string reason,
        string text,
        string running
    )
    {
        var launch = Launch(text, running);

        Assert.Equal(reason, launch.Reading.Reason);
        Assert.True(launch.CompatibilityMode);
        Assert.False(launch.Uploads);
    }

    [Fact]
    public void A_record_file_the_app_did_not_write_uploads_nothing()
    {
        File.WriteAllText(
            Path.Combine(_root, "BazaarPlusPlus.launch.json"),
            new string(' ', 65 * 1024)
                + Golden(record => record["processId"] = Environment.ProcessId)
        );

        var launch = AndroidLaunch.Read(_root, GoldenRelease);

        Assert.True(launch.CompatibilityMode);
        Assert.False(launch.Uploads);
        Assert.Null(launch.RuntimeDigest);
    }

    // The Android App stages the Managed Payload (root ADR 0012): the host never downloads one,
    // and compares a release with the runtime the App started, not with what the installed Mods
    // were staged for.
    [Fact]
    public void Self_update_stages_nothing_and_compares_with_the_launch_record_runtime()
    {
        var installRoot = Path.Combine(_root, "files");
        var dataRoot = Path.Combine(installRoot, "BazaarPlusPlusV6");
        WriteInstalledManifest(installRoot, new string('a', 64));
        var release = Release(GoldenDigest);
        var result = ReleaseManifestFetchResult.Success("6.1.1", release);

        var inApp = AndroidVersionCheck.CreateSelfUpdate(installRoot, dataRoot, GoldenDigest);
        Assert.False(inApp.StagesManagedPayload);
        var evaluation = inApp.Evaluate("6.1.0", result);
        Assert.Equal(SelfUpdateDecision.Stage, evaluation.Decision);
        inApp.Apply(evaluation);
        Assert.Equal(
            MainMenuUpdateStatus.RestartFromApp,
            MainMenuVersionUpdateState.Current.Status
        );
        Assert.False(Directory.Exists(SelfUpdatePaths.Updates(dataRoot)));

        // Outside the App the installed manifest's runtime is the one compared.
        var outside = AndroidVersionCheck.CreateSelfUpdate(installRoot, dataRoot, null);
        var manual = outside.Evaluate("6.1.0", result);
        Assert.Equal(SelfUpdateDecision.ManualUpdate, manual.Decision);
        outside.Apply(manual);
        Assert.Equal(MainMenuUpdateStatus.ManualUpdate, MainMenuVersionUpdateState.Current.Status);
    }

    [Theory]
    [InlineData(SelfUpdateDecision.UpToDate, MainMenuUpdateStatus.None)]
    [InlineData(SelfUpdateDecision.ManualUpdate, MainMenuUpdateStatus.ManualUpdate)]
    [InlineData(SelfUpdateDecision.Staged, MainMenuUpdateStatus.RestartToUpdate)]
    [InlineData(SelfUpdateDecision.Stage, MainMenuUpdateStatus.RestartFromApp)]
    public void A_host_that_does_not_stage_never_downloads(
        SelfUpdateDecision decision,
        MainMenuUpdateStatus status
    )
    {
        Assert.Equal(status, MainMenuSelfUpdate.StatusFor(decision, hostStages: false));
        Assert.Null(MainMenuSelfUpdate.StatusFor(SelfUpdateDecision.Stage, hostStages: true));
    }

    private static ManagedPayloadRelease Release(string digest) =>
        new(
            "6.1.1",
            new Uri("https://cdn.example/6.1.1.zip"),
            10,
            new string('b', 64),
            "sig",
            RunLogSchema.LocalDatabaseSchemaVersion,
            digest
        );

    private static void WriteInstalledManifest(string installRoot, string digest)
    {
        var path = SelfUpdatePaths.InstalledManifest(installRoot, SelfUpdateHost.Android);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            "bpp-managed-payload-manifest 1\n"
                + "version 6.1.0\n"
                + "platform android-aarch64\n"
                + $"history-database-user-version {RunLogSchema.LocalDatabaseSchemaVersion}\n"
                + $"non-managed-payload-digest {digest}\n"
                + $"file {new string('c', 64)} 1 MelonLoader/Mods/BazaarPlusPlus.dll\n"
        );
    }
}
