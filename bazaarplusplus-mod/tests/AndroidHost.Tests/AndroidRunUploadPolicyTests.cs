#nullable enable
using BazaarPlusPlus.Android;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// Compatibility Mode never lets its Runs upload later (root ADR 0012, "宁可少传"): over a
/// temporary history database in the host's DELETE journal mode, the record chain's Run store
/// that <see cref="AndroidComposition"/> composes for a Compatibility Mode launch writes
/// History-Only Runs, the launch demotes a Run a normal launch left active, and the seal
/// worker's job discovery creates no job for either; a normal launch's store writes
/// <c>bundle</c> Runs, which it does seal.
/// </summary>
public sealed class AndroidRunUploadPolicyTests : IDisposable
{
    private static readonly DateTimeOffset Started = DateTimeOffset.FromUnixTimeMilliseconds(
        1767225600000
    );

    private readonly string _dataRoot = Directory
        .CreateTempSubdirectory("bpp-android-runs-")
        .FullName;

    private IPathProvider Paths => new DataRoot(_dataRoot);

    private string Database => PathConstants.RunLogDatabase(_dataRoot);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataRoot, recursive: true);
    }

    private static AndroidLaunch Compatibility() =>
        AndroidLaunchTests.Launch(
            AndroidLaunchTests.Golden(record => record["mode"] = "compatibility")
        );

    private static AndroidLaunch Normal() => AndroidLaunchTests.Launch(AndroidLaunchTests.Golden());

    [Fact]
    public void A_compatibility_mode_store_writes_history_only_runs_that_are_never_sealed()
    {
        var store = AndroidComposition.CreateRunLogStore(Paths, Compatibility());

        var created = store.CreateRun(Request("run-compat"));
        Assert.Equal("run-compat", created.RunId);
        Assert.Equal("history_only", Policy("run-compat"));
        store.CompleteRun("run-compat", Completed());

        EnsureJobs();
        Assert.Equal(0L, SealJobs());
    }

    [Fact]
    public void A_compatibility_mode_launch_demotes_the_active_run_and_does_not_resume_it()
    {
        var normal = AndroidComposition.CreateRunLogStore(Paths, Normal());
        normal.CreateRun(Request("run-left-active"));
        normal.CreateRun(Request("run-finished"));
        normal.CompleteRun("run-finished", Completed());

        Assert.Equal(1, AndroidComposition.PrepareRuns(Paths, Compatibility()));

        Assert.Equal("history_only", Policy("run-left-active"));
        Assert.Equal("bundle", Policy("run-finished"));
        var compat = AndroidComposition.CreateRunLogStore(Paths, Compatibility());
        Assert.Null(compat.TryResumeActiveRun());
        // The game's Run id comes back: a new History-Only Run starts beside the demoted one.
        var continued = compat.CreateRun(Request("run-left-active"));
        Assert.NotEqual("run-left-active", continued.RunId);
        Assert.Equal("history_only", Policy(continued.RunId));
        compat.CompleteRun(continued.RunId, Completed());
        CompleteDirectly("run-left-active");

        EnsureJobs();
        // Only the Run the normal launch finished is sealed.
        Assert.Equal(1L, SealJobs());
        Assert.Equal(1L, SealJobs("run-finished"));
    }

    [Fact]
    public void A_normal_launch_writes_bundle_runs_and_demotes_nothing()
    {
        var store = AndroidComposition.CreateRunLogStore(Paths, Normal());
        store.CreateRun(Request("run-normal"));

        Assert.Equal(0, AndroidComposition.PrepareRuns(Paths, Normal()));
        Assert.Equal("bundle", Policy("run-normal"));
        Assert.Equal("run-normal", store.TryResumeActiveRun()?.RunId);
        store.CompleteRun("run-normal", Completed());

        EnsureJobs();
        Assert.Equal(1L, SealJobs("run-normal"));
    }

    private static RunLogCreateRequest Request(string runId) =>
        new()
        {
            RunId = runId,
            StartedAtUtc = Started,
            Hero = "Vanessa",
            GameMode = "Ranked",
            ModVersion = "6.1.0",
        };

    private static RunLogCompletion Completed() =>
        new() { EndedAtUtc = Started.AddMinutes(30), Status = "completed" };

    private void EnsureJobs() =>
        new BundleQueueStore(Database, AndroidComposition.DatabaseOptions).EnsureEligibleJobs(
            TimeSpan.Zero
        );

    // A Run left active ends as abandoned, which is never sealable anyway; marking the demoted
    // one completed shows that its policy alone keeps it out.
    private void CompleteDirectly(string runId) =>
        Execute($"UPDATE runs SET status = 'completed' WHERE run_id = '{runId}';");

    private string? Policy(string runId) =>
        Scalar($"SELECT upload_policy FROM runs WHERE run_id = '{runId}';") as string;

    private long SealJobs(string? runId = null) =>
        (long)
            Scalar(
                runId == null
                    ? "SELECT COUNT(*) FROM bundle_seal_jobs;"
                    : $"SELECT COUNT(*) FROM bundle_seal_jobs WHERE run_id = '{runId}';"
            )!;

    private object? Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class DataRoot(string path) : IPathProvider
    {
        public string? DataRootDirectoryPath => path;
    }
}
