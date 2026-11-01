#nullable enable
using System.Text.Json.Nodes;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.Updater;
using Xunit;

namespace Updater.Tests;

/// <summary>
/// The Launch Record reader over the Android App's golden (root AGENTS.md, Contracts) and its
/// mutations: what the mod and the Updater make of a record (root ADR 0012, "启动记录").
/// </summary>
public sealed class LaunchRecordTests : IDisposable
{
    private const int GoldenProcessId = 12345;
    private const string GoldenRelease = "6.1.0";

    private readonly string _root = Directory.CreateTempSubdirectory("bpp-launch-record-").FullName;
    private readonly List<string> _log = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    internal static string Golden()
    {
        using var stream =
            typeof(LaunchRecordTests).Assembly.GetManifestResourceStream(
                "Updater.Tests.launch-record.v1.json"
            ) ?? throw new InvalidOperationException("The Launch Record golden is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static string Mutated(Action<JsonObject> mutate)
    {
        var record = JsonNode.Parse(Golden())!.AsObject();
        mutate(record);
        return record.ToJsonString();
    }

    [Fact]
    public void The_golden_is_a_normal_record_of_its_process_and_release()
    {
        var reading = LaunchRecordReading.Classify(Golden(), GoldenProcessId, GoldenRelease);

        Assert.Equal(LaunchRecordMode.Normal, reading.Mode);
        Assert.Equal("normal", reading.Reason);
        var record = reading.Record!;
        Assert.Equal(GoldenProcessId, record.ProcessId);
        Assert.Equal(GoldenRelease, record.Release);
        Assert.False(record.IsCompatibilityMode);
        Assert.Equal("com.bazaarplusplus.android", record.App.PackageName);
        Assert.Equal(6001000, record.App.VersionCode);
        Assert.Equal(
            "b022c26b660889ad3a1026a358432a2857ae3f9f5e5495f04eb21afc14242c09",
            record.App.NonManagedPayloadDigest
        );
        Assert.Equal("com.tempogames.thebazaar", record.Game.PackageName);
        Assert.Equal(12825, record.Game.VersionCode);
        Assert.Equal(64, record.Game.Il2CppSha256.Length);
        Assert.Equal(64, record.Game.MetadataSha256.Length);
        Assert.Equal("release", record.Interop.Source);
        Assert.Equal(64, record.Interop.SnapshotSha256.Length);
    }

    [Fact]
    public void No_file_means_the_mod_is_not_in_the_android_app()
    {
        var reading = LaunchRecordReading.Read(
            Path.Combine(_root, LaunchRecord.FileName),
            GoldenProcessId,
            GoldenRelease
        );

        Assert.Equal(LaunchRecordMode.Absent, reading.Mode);
        Assert.Equal("absent", reading.Reason);
        Assert.Null(reading.Record);
    }

    [Fact]
    public void The_file_is_read_from_user_data()
    {
        File.WriteAllText(LaunchRecord.PathIn(_root), Golden());

        var reading = LaunchRecordReading.Read(
            LaunchRecord.PathIn(_root),
            GoldenProcessId,
            GoldenRelease
        );

        Assert.Equal(LaunchRecordMode.Normal, reading.Mode);
        Assert.Equal(Path.Combine(_root, "BazaarPlusPlus.launch.json"), LaunchRecord.PathIn(_root));
    }

    [Fact]
    public void A_file_that_is_not_json_is_compatibility_mode()
    {
        File.WriteAllText(LaunchRecord.PathIn(_root), "{ \"schemaVersion\": 1, ");

        var reading = LaunchRecordReading.Read(
            LaunchRecord.PathIn(_root),
            GoldenProcessId,
            GoldenRelease
        );

        Assert.Equal(LaunchRecordMode.Compatibility, reading.Mode);
        Assert.Equal("invalid_json", reading.Reason);
        Assert.Null(reading.Record);
    }

    [Fact]
    public void Another_process_s_record_is_compatibility_mode_and_names_no_runtime()
    {
        var reading = LaunchRecordReading.Classify(Golden(), GoldenProcessId + 1, GoldenRelease);

        Assert.Equal(LaunchRecordMode.Compatibility, reading.Mode);
        Assert.Equal("process_mismatch", reading.Reason);
        Assert.Null(reading.Record);
    }

    [Theory]
    [InlineData("6.0.2")]
    [InlineData("6.1.1")]
    [InlineData("not-a-version")]
    public void Another_release_is_compatibility_mode_but_keeps_the_runtime(string running)
    {
        var reading = LaunchRecordReading.Classify(Golden(), GoldenProcessId, running);

        Assert.Equal(LaunchRecordMode.Compatibility, reading.Mode);
        Assert.Equal("release_mismatch", reading.Reason);
        Assert.NotNull(reading.Record);
    }

    [Fact]
    public void The_release_check_is_skipped_when_no_mods_run_yet()
    {
        var reading = LaunchRecordReading.Classify(Golden(), GoldenProcessId, runningRelease: null);

        Assert.Equal(LaunchRecordMode.Normal, reading.Mode);
    }

    [Fact]
    public void Mode_compatibility_is_compatibility_mode_with_the_record()
    {
        var reading = LaunchRecordReading.Classify(
            Mutated(record => record["mode"] = "compatibility"),
            GoldenProcessId,
            GoldenRelease
        );

        Assert.Equal(LaunchRecordMode.Compatibility, reading.Mode);
        Assert.Equal("mode_compatibility", reading.Reason);
        Assert.True(reading.Record!.IsCompatibilityMode);
    }

    [Fact]
    public void A_development_app_has_no_runtime_digest()
    {
        var reading = LaunchRecordReading.Classify(
            Mutated(record => record["app"]!["nonManagedPayloadDigest"] = null),
            GoldenProcessId,
            GoldenRelease
        );

        Assert.Equal(LaunchRecordMode.Normal, reading.Mode);
        Assert.Null(reading.Record!.App.NonManagedPayloadDigest);
    }

    public static TheoryData<string, string> Untrusted =>
        new()
        {
            { "schema_unknown", Mutated(record => record["schemaVersion"] = 2) },
            { "invalid", Mutated(record => record.Remove("schemaVersion")) },
            { "mode_unknown", Mutated(record => record["mode"] = "turbo") },
            { "invalid", Mutated(record => record.Remove("mode")) },
            { "invalid", Mutated(record => record["processId"] = "12345") },
            { "invalid", Mutated(record => record["release"] = "6.1") },
            { "invalid", Mutated(record => record["extra"] = true) },
            { "invalid", Mutated(record => record["app"]!.AsObject().Remove("versionCode")) },
            {
                "invalid",
                Mutated(record => record["app"]!["nonManagedPayloadDigest"] = new string('A', 64))
            },
            { "invalid", Mutated(record => record["game"]!["il2cppSha256"] = "abc") },
            { "invalid", Mutated(record => record["interop"]!["source"] = "device") },
            { "invalid", Mutated(record => record["interop"] = "release") },
            { "invalid_json", "[" },
            { "invalid", "[]" },
        };

    [Theory]
    [MemberData(nameof(Untrusted))]
    public void A_record_that_does_not_parse_is_compatibility_mode(string reason, string text)
    {
        var reading = LaunchRecordReading.Classify(text, GoldenProcessId, GoldenRelease);

        Assert.Equal(LaunchRecordMode.Compatibility, reading.Mode);
        Assert.Equal(reason, reading.Reason);
        Assert.Null(reading.Record);
    }

    [Fact]
    public void The_updater_takes_the_runtime_digest_of_its_own_process_only()
    {
        var path = LaunchRecord.PathIn(_root);
        File.WriteAllText(path, Mutated(record => record["processId"] = Environment.ProcessId));

        Assert.Equal(
            "b022c26b660889ad3a1026a358432a2857ae3f9f5e5495f04eb21afc14242c09",
            UpdaterRun.RuntimeDigest(path, Environment.ProcessId, Log())
        );
        Assert.Contains(
            _log,
            line =>
                line.Contains(
                    "event=updater.launch_record.read mode=Normal reason=normal runtime_digest=present",
                    StringComparison.Ordinal
                )
        );
        Assert.Null(UpdaterRun.RuntimeDigest(path, Environment.ProcessId + 1, Log()));
        // The record names the release this launch may swap in, so a stale one is not checked.
        File.WriteAllText(
            path,
            Mutated(record =>
            {
                record["processId"] = Environment.ProcessId;
                record["release"] = "9.9.9";
            })
        );
        Assert.NotNull(UpdaterRun.RuntimeDigest(path, Environment.ProcessId, Log()));
        File.Delete(path);
        Assert.Null(UpdaterRun.RuntimeDigest(path, Environment.ProcessId, Log()));
    }

    private UpdaterLog Log() => new(_log.Add, _log.Add, _log.Add);
}
