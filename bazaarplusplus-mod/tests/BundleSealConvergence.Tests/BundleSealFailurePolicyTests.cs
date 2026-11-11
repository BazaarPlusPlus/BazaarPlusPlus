using System.Data.Common;
using System.Reflection;
using BazaarPlusPlus.Game.BundlePipeline;
using Xunit;

// Not BundleSealConvergence.Tests: that namespace would hide the Core type of the same name.
namespace BundleSeal.Tests;

// Each case table row of the capsule is one Theory row, keyed by the row's name so every row
// reports on its own; the row itself stays in the table below.
public sealed class BundleSealFailurePolicyTests
{
    private const string NewtonsoftIdentity =
        "Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed";
    private const float Day = 24f * 3600f;

    private const string Blocked = BundleSealFailurePolicy.EnvironmentBlockedCode;
    private const BundleSealFailureCause Environment = BundleSealFailureCause.Environment;
    private const BundleSealFailureCause Transient = BundleSealFailureCause.Transient;
    private const BundleSealFailureCause Other = BundleSealFailureCause.Other;
    private const BundleSealFailureDisposition Terminal = BundleSealFailureDisposition.Terminal;
    private const BundleSealFailureDisposition Park =
        BundleSealFailureDisposition.ParkUntilNextLaunch;
    private const BundleSealFailureDisposition Retry = BundleSealFailureDisposition.RetryLater;
    private const BundleSealFailureDisposition Degrade = BundleSealFailureDisposition.Degrade;
    private const BundleSealFailureDisposition Reseal = BundleSealFailureDisposition.Reseal;
    private const BundleSealFailureDisposition Skip = BundleSealFailureDisposition.Skip;

    private static readonly Dictionary<
        string,
        (Exception? Exception, BundleSealFailureCause Expected)
    > ClassifyCases = Table<(Exception?, BundleSealFailureCause)>(
        ("missing method", (new MissingMethodException("JToken.ToString"), Environment)),
        ("missing field", (new MissingFieldException("field"), Environment)),
        ("type load", (new TypeLoadException("type"), Environment)),
        ("bad image", (new BadImageFormatException("image"), Environment)),
        ("native entry point", (new EntryPointNotFoundException("native"), Environment)),
        ("native library", (new DllNotFoundException("native"), Environment)),
        (
            "type initializer is cached for the process",
            (new TypeInitializationException("Codec", new InvalidDataException()), Environment)
        ),
        ("assembly load", (new FileLoadException("load", NewtonsoftIdentity), Environment)),
        (
            "assembly missing",
            (new FileNotFoundException("missing", NewtonsoftIdentity), Environment)
        ),
        (
            "missing file path is file IO",
            (new FileNotFoundException("missing", "/outbox/01J.bundle"), Transient)
        ),
        ("io", (new IOException("locked"), Transient)),
        ("access", (new UnauthorizedAccessException(), Transient)),
        ("database busy", (new FakeDbException(), Transient)),
        ("unrequested cancellation", (new OperationCanceledException(), Transient)),
        ("codec", (new InvalidDataException("codec"), Other)),
        ("unknown", (new InvalidOperationException("bug"), Other)),
        ("observed invalid artifact", (null, Other)),
        (
            "inner environment link",
            (
                new InvalidOperationException("wrap", new MissingMethodException("inner")),
                Environment
            )
        ),
        (
            "reflection wrapper",
            (new TargetInvocationException(new TypeLoadException("inner")), Environment)
        ),
        (
            "environment wins over transient in an aggregate",
            (
                new AggregateException(new IOException("first"), new MissingMethodException()),
                Environment
            )
        ),
        (
            "inner transient link",
            (new InvalidDataException("wrap", new IOException("inner")), Transient)
        )
    );

    public static TheoryData<string> ClassifyCaseNames => Names(ClassifyCases);

    [Theory]
    [MemberData(nameof(ClassifyCaseNames))]
    public void Classifies_every_link_of_the_exception_graph(string name)
    {
        var (exception, expected) = ClassifyCases[name];
        Assert.Equal(expected, BundleSealFailurePolicy.Classify(exception));
    }

    private static readonly BundleSealRetryFacts Fresh = new(null, 0, 10f);
    private static readonly MissingMethodException Missing = new(
        "Newtonsoft.Json.Linq.JToken.ToString"
    );
    private static readonly IOException Io = new("locked");
    private static readonly InvalidDataException Codec = new("bundle_id");

    private static readonly Dictionary<
        string,
        (
            BundleSealStage Stage,
            Exception? Exception,
            BundleSealFailureDisposition Disposition,
            BundleSealFailureLog Log,
            string Code
        )
    > DecideCases = Table<(
        BundleSealStage,
        Exception?,
        BundleSealFailureDisposition,
        BundleSealFailureLog,
        string
    )>(
        (
            "Build/codec",
            (
                BundleSealStage.Build,
                Codec,
                Terminal,
                BundleSealFailureLog.Terminal,
                "bundle_build_failed"
            )
        ),
        (
            "PayloadEncode/codec",
            (
                BundleSealStage.PayloadEncode,
                Codec,
                Terminal,
                BundleSealFailureLog.Terminal,
                "payload_encode_failed"
            )
        ),
        (
            "Build/missing",
            (BundleSealStage.Build, Missing, Park, BundleSealFailureLog.EnvironmentBlocked, Blocked)
        ),
        (
            "Composition/missing",
            (
                BundleSealStage.Composition,
                Missing,
                Park,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            )
        ),
        (
            "Composition/codec",
            (
                BundleSealStage.Composition,
                Codec,
                Retry,
                BundleSealFailureLog.Deferred,
                "payload_compose_failed"
            )
        ),
        (
            "Publish/io",
            (
                BundleSealStage.Publish,
                Io,
                Retry,
                BundleSealFailureLog.Deferred,
                "seal_publish_failed"
            )
        ),
        (
            "Allocation/io",
            (
                BundleSealStage.Allocation,
                Io,
                Retry,
                BundleSealFailureLog.Deferred,
                "seal_allocation_failed"
            )
        ),
        (
            "Screenshot/codec",
            (
                BundleSealStage.Screenshot,
                Codec,
                Retry,
                BundleSealFailureLog.Deferred,
                "screenshot_encode_failed"
            )
        ),
        (
            "Attempt/codec",
            (
                BundleSealStage.Attempt,
                Codec,
                Retry,
                BundleSealFailureLog.Deferred,
                "seal_attempt_failed"
            )
        ),
        (
            "ReplayRead/missing",
            (
                BundleSealStage.ReplayRead,
                Missing,
                Park,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            )
        ),
        (
            "ReplayRead/io",
            (
                BundleSealStage.ReplayRead,
                Io,
                Retry,
                BundleSealFailureLog.Deferred,
                "replay_unreadable"
            )
        ),
        (
            "ReplayDecode/wrapped missing",
            (
                BundleSealStage.ReplayDecode,
                new InvalidOperationException("wrapper", Missing),
                Park,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            )
        ),
        (
            "ReplayDecode/codec",
            (
                BundleSealStage.ReplayDecode,
                Codec,
                Degrade,
                BundleSealFailureLog.Degraded,
                "replay_invalid"
            )
        ),
        (
            "ReplayDecode/observed",
            (
                BundleSealStage.ReplayDecode,
                null,
                Degrade,
                BundleSealFailureLog.Degraded,
                "replay_invalid"
            )
        ),
        (
            // Truncated MessagePack: its EndOfStreamException is an IOException, yet the bytes
            // will never decode.
            "ReplayDecode/truncated MessagePack",
            (
                BundleSealStage.ReplayDecode,
                new InvalidDataException("wrap", new EndOfStreamException()),
                Degrade,
                BundleSealFailureLog.Degraded,
                "replay_invalid"
            )
        ),
        (
            // Mono reports a corrupt gzip stream as IOException.
            "ReplayDecode/io",
            (
                BundleSealStage.ReplayDecode,
                Io,
                Degrade,
                BundleSealFailureLog.Degraded,
                "replay_invalid"
            )
        ),
        (
            "ScreenshotTerminal/io",
            (
                BundleSealStage.ScreenshotTerminal,
                Io,
                Skip,
                BundleSealFailureLog.MaintenanceFailed,
                "screenshot_terminal_failed"
            )
        ),
        (
            "PendingValidation/missing",
            (
                BundleSealStage.PendingValidation,
                Missing,
                Skip,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            )
        ),
        (
            "PendingValidation/io",
            (
                BundleSealStage.PendingValidation,
                Io,
                Skip,
                BundleSealFailureLog.Deferred,
                "pending_file_unreadable"
            )
        ),
        (
            "PendingValidation/codec",
            (
                BundleSealStage.PendingValidation,
                Codec,
                Reseal,
                BundleSealFailureLog.Degraded,
                "pending_file_invalid"
            )
        ),
        (
            "PendingValidation/observed",
            (
                BundleSealStage.PendingValidation,
                null,
                Reseal,
                BundleSealFailureLog.Degraded,
                "pending_file_invalid"
            )
        ),
        (
            "OrphanAdoption/missing",
            (
                BundleSealStage.OrphanAdoption,
                Missing,
                Skip,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            )
        ),
        (
            "OrphanAdoption/io",
            (
                BundleSealStage.OrphanAdoption,
                Io,
                Skip,
                BundleSealFailureLog.Deferred,
                "orphan_file_unreadable"
            )
        ),
        (
            "OrphanAdoption/codec",
            (
                BundleSealStage.OrphanAdoption,
                Codec,
                Degrade,
                BundleSealFailureLog.Degraded,
                "orphan_file_invalid"
            )
        ),
        (
            "FileRecovery/io",
            (
                BundleSealStage.FileRecovery,
                Io,
                Skip,
                BundleSealFailureLog.MaintenanceFailed,
                "file_recovery_failed"
            )
        ),
        (
            "JobRead/codec",
            (
                BundleSealStage.JobRead,
                Codec,
                Skip,
                BundleSealFailureLog.MaintenanceFailed,
                "seal_job_unreadable"
            )
        ),
        (
            "JobDiscovery/missing",
            (
                BundleSealStage.JobDiscovery,
                Missing,
                Skip,
                BundleSealFailureLog.EnvironmentBlocked,
                "job_discovery_failed"
            )
        )
    );

    public static TheoryData<string> DecideCaseNames => Names(DecideCases);

    [Theory]
    [MemberData(nameof(DecideCaseNames))]
    public void Decides_each_stage_and_cause(string name)
    {
        var (stage, exception, disposition, log, code) = DecideCases[name];
        var decision = BundleSealFailurePolicy.Decide(stage, exception, Fresh);
        Assert.Equal(disposition, decision.Disposition);
        Assert.Equal(log, decision.Log);
        Assert.Equal(code, decision.Code);
    }

    [Fact]
    public void Detail_names_the_environment_link_not_its_wrapper() =>
        Assert.Equal(
            "System.MissingMethodException: Newtonsoft.Json.Linq.JToken.ToString",
            BundleSealFailurePolicy
                .Decide(
                    BundleSealStage.Build,
                    new InvalidOperationException("wrapper", Missing),
                    Fresh
                )
                .Detail
        );

    [Fact]
    public void Persisted_diagnostic_stays_bounded() =>
        Assert.Equal(
            512,
            BundleSealFailurePolicy
                .Decide(
                    BundleSealStage.Build,
                    new InvalidDataException(new string('x', 4096)),
                    Fresh
                )
                .Detail!.Length
        );

    private static readonly Dictionary<string, RetryCase> RetryCases = Table<RetryCase>(
        (
            "many failures of an old job exhaust",
            new(
                BundleSealStage.Attempt,
                Io,
                new("seal_attempt_failed", 7, 15 * Day),
                Terminal,
                "seal_retry_exhausted",
                8
            )
        ),
        (
            "one attempt short keeps retrying",
            new(
                BundleSealStage.Attempt,
                Io,
                new("seal_attempt_failed", 6, 15 * Day),
                Retry,
                "seal_attempt_failed",
                7
            )
        ),
        (
            "a young job keeps retrying",
            new(
                BundleSealStage.Attempt,
                Io,
                new("seal_attempt_failed", 7, 13 * Day),
                Retry,
                "seal_attempt_failed",
                8
            )
        ),
        (
            "a revived job's first failure never exhausts",
            new(
                BundleSealStage.Composition,
                new InvalidOperationException(),
                new(null, 1, 40 * Day),
                Retry,
                "payload_compose_failed",
                1
            )
        ),
        (
            "a different failure restarts the consecutive count",
            new(
                BundleSealStage.Publish,
                Io,
                new("payload_compose_failed", 7, 15 * Day),
                Retry,
                "seal_publish_failed",
                1
            )
        ),
        (
            "an exhausted replay read omits that replay instead of losing the Run",
            new(
                BundleSealStage.ReplayRead,
                Io,
                new("replay_unreadable", 7, 15 * Day),
                Degrade,
                "replay_unreadable",
                8
            )
        ),
        (
            "past the age ceiling, alternating codes cannot keep a job alive",
            new(
                BundleSealStage.Composition,
                Io,
                new("seal_publish_failed", 7, 91 * Day),
                Terminal,
                "seal_retry_exhausted",
                1
            )
        ),
        (
            "the age ceiling never ends a job on its first failure",
            new(
                BundleSealStage.Composition,
                Io,
                new(null, 1, 120 * Day),
                Retry,
                "payload_compose_failed",
                1
            )
        ),
        (
            "below the age ceiling, attempts still decide",
            new(
                BundleSealStage.Composition,
                Io,
                new("payload_compose_failed", 2, 89 * Day),
                Retry,
                "payload_compose_failed",
                3
            )
        )
    );

    public static TheoryData<string> RetryCaseNames => Names(RetryCases);

    [Theory]
    [MemberData(nameof(RetryCaseNames))]
    public void Bounds_retries_by_attempts_and_age(string name) => Expect(RetryCases[name]);

    private static readonly MissingMethodException ParkedMissing = new();

    private static readonly Dictionary<string, RetryCase> ParkedCases = Table<RetryCase>(
        (
            "parked across five launches and sixty days expires",
            new(
                BundleSealStage.Build,
                ParkedMissing,
                new(Blocked, 4, 61 * Day),
                Terminal,
                BundleSealFailurePolicy.EnvironmentBlockedExpiredCode,
                5
            )
        ),
        (
            "fewer launches stay parked",
            new(BundleSealStage.Build, ParkedMissing, new(Blocked, 3, 61 * Day), Park, Blocked, 4)
        ),
        (
            "younger than sixty days stays parked",
            new(BundleSealStage.Build, ParkedMissing, new(Blocked, 10, 59 * Day), Park, Blocked, 11)
        ),
        (
            "transient attempts do not count as parked launches",
            new(
                BundleSealStage.Build,
                ParkedMissing,
                new("seal_publish_failed", 9, 61 * Day),
                Park,
                Blocked,
                1
            )
        ),
        (
            "past the age ceiling a parked job ends without five launches",
            new(
                BundleSealStage.Build,
                ParkedMissing,
                new("seal_publish_failed", 1, 91 * Day),
                Terminal,
                BundleSealFailurePolicy.EnvironmentBlockedExpiredCode,
                1
            )
        )
    );

    public static TheoryData<string> ParkedCaseNames => Names(ParkedCases);

    [Theory]
    [MemberData(nameof(ParkedCaseNames))]
    public void Expires_parked_jobs_only_after_launches_and_age(string name) =>
        Expect(ParkedCases[name]);

    private static readonly Dictionary<
        string,
        (string? Code, int Attempts, float? SinceAttempt, float SinceLaunch, bool Due, float? Until)
    > DueCases = Table<(string?, int, float?, float, bool, float?)>(
        ("never failed", (null, 0, null, 0f, true, 0f)),
        ("parked in an earlier launch", (Blocked, 2, 100f, 50f, true, 0f)),
        ("parked in this launch", (Blocked, 2, 10f, 50f, false, null)),
        ("clock moved backwards", (Blocked, 2, -5f, 50f, true, 0f)),
        ("first backoff", ("seal_publish_failed", 1, 4.9f, 0f, false, 0.1f)),
        ("first backoff elapsed", ("seal_publish_failed", 1, 5f, 0f, true, 0f)),
        ("third backoff", ("seal_publish_failed", 3, 19f, 0f, false, 1f)),
        ("third backoff elapsed", ("seal_publish_failed", 3, 20f, 0f, true, 0f)),
        ("backoff cap", ("seal_publish_failed", 40, 3599f, 0f, false, 1f)),
        ("backoff cap elapsed", ("seal_publish_failed", 40, 3600f, 0f, true, 0f)),
        ("no recorded attempts", ("seal_publish_failed", 0, 0f, 0f, true, 0f))
    );

    public static TheoryData<string> DueCaseNames => Names(DueCases);

    [Theory]
    [MemberData(nameof(DueCaseNames))]
    public void Schedules_due_jobs(string name)
    {
        var (code, attempts, sinceAttempt, sinceLaunch, due, until) = DueCases[name];
        // The wake schedule reads the same backoff: due exactly when nothing is left of it.
        var left = BundleSealFailurePolicy.SecondsUntilDue(
            code,
            attempts,
            sinceAttempt,
            sinceLaunch
        );
        Assert.True(
            until is { } expected
                ? left is { } actual && Math.Abs(actual - expected) < 0.001f
                : left == null,
            $"seconds until due {name}: expected {until}, actual {left}"
        );
        Assert.Equal(due, BundleSealFailurePolicy.IsDue(code, attempts, sinceAttempt, sinceLaunch));
    }

    private static void Expect(RetryCase row)
    {
        var decision = BundleSealFailurePolicy.Decide(row.Stage, row.Exception, row.Retry);
        Assert.Equal(row.Disposition, decision.Disposition);
        Assert.Equal(row.Code, decision.Code);
        Assert.Equal(row.Attempts, decision.Attempts);
    }

    private static Dictionary<string, T> Table<T>(params (string Name, T Row)[] rows) =>
        rows.ToDictionary(row => row.Name, row => row.Row, StringComparer.Ordinal);

    private static TheoryData<string> Names<T>(Dictionary<string, T> table) => new(table.Keys);

    private sealed record RetryCase(
        BundleSealStage Stage,
        Exception Exception,
        BundleSealRetryFacts Retry,
        BundleSealFailureDisposition Disposition,
        string Code,
        int Attempts
    );

    private sealed class FakeDbException : DbException { }
}
