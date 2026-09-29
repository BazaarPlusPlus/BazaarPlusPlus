#nullable enable
using System.Data.Common;
using System.Reflection;
using BazaarPlusPlus.Game.BundlePipeline;

internal static class BundleSealFailurePolicyTests
{
    private const string NewtonsoftIdentity =
        "Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed";
    private const float Day = 24f * 3600f;

    internal static void Run()
    {
        ClassifiesEveryLinkOfTheExceptionGraph();
        DecidesEachStageAndCause();
        BoundsRetriesByAttemptsAndAge();
        ExpiresParkedJobsOnlyAfterLaunchesAndAge();
        SchedulesDueJobs();
    }

    private static void ClassifiesEveryLinkOfTheExceptionGraph()
    {
        var cases = new (Exception? Exception, BundleSealFailureCause Expected, string Name)[]
        {
            (new MissingMethodException("JToken.ToString"), Environment, "missing method"),
            (new MissingFieldException("field"), Environment, "missing field"),
            (new TypeLoadException("type"), Environment, "type load"),
            (new BadImageFormatException("image"), Environment, "bad image"),
            (new EntryPointNotFoundException("native"), Environment, "native entry point"),
            (new DllNotFoundException("native"), Environment, "native library"),
            (
                new TypeInitializationException("Codec", new InvalidDataException()),
                Environment,
                "type initializer is cached for the process"
            ),
            (new FileLoadException("load", NewtonsoftIdentity), Environment, "assembly load"),
            (
                new FileNotFoundException("missing", NewtonsoftIdentity),
                Environment,
                "assembly missing"
            ),
            (
                new FileNotFoundException("missing", "/outbox/01J.bundle"),
                Transient,
                "missing file path is file IO"
            ),
            (new IOException("locked"), Transient, "io"),
            (new UnauthorizedAccessException(), Transient, "access"),
            (new FakeDbException(), Transient, "database busy"),
            (new OperationCanceledException(), Transient, "unrequested cancellation"),
            (new InvalidDataException("codec"), Other, "codec"),
            (new InvalidOperationException("bug"), Other, "unknown"),
            (null, Other, "observed invalid artifact"),
            (
                new InvalidOperationException("wrap", new MissingMethodException("inner")),
                Environment,
                "inner environment link"
            ),
            (
                new TargetInvocationException(new TypeLoadException("inner")),
                Environment,
                "reflection wrapper"
            ),
            (
                new AggregateException(new IOException("first"), new MissingMethodException()),
                Environment,
                "environment wins over transient in an aggregate"
            ),
            (
                new InvalidDataException("wrap", new IOException("inner")),
                Transient,
                "inner transient link"
            ),
        };
        foreach (var (exception, expected, name) in cases)
            Equal(expected, BundleSealFailurePolicy.Classify(exception), "classify " + name);
    }

    private static void DecidesEachStageAndCause()
    {
        var fresh = new BundleSealRetryFacts(null, 0, 10f);
        var missing = new MissingMethodException("Newtonsoft.Json.Linq.JToken.ToString");
        var io = new IOException("locked");
        var codec = new InvalidDataException("bundle_id");
        var cases = new (
            BundleSealStage Stage,
            Exception? Exception,
            BundleSealFailureDisposition Disposition,
            BundleSealFailureLog Log,
            string Code
        )[]
        {
            (
                BundleSealStage.Build,
                codec,
                Terminal,
                BundleSealFailureLog.Terminal,
                "bundle_build_failed"
            ),
            (
                BundleSealStage.PayloadEncode,
                codec,
                Terminal,
                BundleSealFailureLog.Terminal,
                "payload_encode_failed"
            ),
            (
                BundleSealStage.Build,
                missing,
                Park,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            ),
            (
                BundleSealStage.Composition,
                missing,
                Park,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            ),
            (
                BundleSealStage.Composition,
                codec,
                Retry,
                BundleSealFailureLog.Deferred,
                "payload_compose_failed"
            ),
            (
                BundleSealStage.Publish,
                io,
                Retry,
                BundleSealFailureLog.Deferred,
                "seal_publish_failed"
            ),
            (
                BundleSealStage.Allocation,
                io,
                Retry,
                BundleSealFailureLog.Deferred,
                "seal_allocation_failed"
            ),
            (
                BundleSealStage.Screenshot,
                codec,
                Retry,
                BundleSealFailureLog.Deferred,
                "screenshot_encode_failed"
            ),
            (
                BundleSealStage.Attempt,
                codec,
                Retry,
                BundleSealFailureLog.Deferred,
                "seal_attempt_failed"
            ),
            (
                BundleSealStage.ReplayLoad,
                missing,
                Park,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            ),
            (
                BundleSealStage.ReplayLoad,
                io,
                Retry,
                BundleSealFailureLog.Deferred,
                "replay_unreadable"
            ),
            (
                BundleSealStage.ReplayLoad,
                codec,
                Degrade,
                BundleSealFailureLog.Degraded,
                "replay_invalid"
            ),
            (
                BundleSealStage.ReplayLoad,
                null,
                Degrade,
                BundleSealFailureLog.Degraded,
                "replay_invalid"
            ),
            (
                BundleSealStage.PendingValidation,
                missing,
                Skip,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            ),
            (
                BundleSealStage.PendingValidation,
                io,
                Skip,
                BundleSealFailureLog.Deferred,
                "pending_file_unreadable"
            ),
            (
                BundleSealStage.PendingValidation,
                codec,
                Reseal,
                BundleSealFailureLog.Degraded,
                "pending_file_invalid"
            ),
            (
                BundleSealStage.PendingValidation,
                null,
                Reseal,
                BundleSealFailureLog.Degraded,
                "pending_file_invalid"
            ),
            (
                BundleSealStage.OrphanAdoption,
                missing,
                Skip,
                BundleSealFailureLog.EnvironmentBlocked,
                Blocked
            ),
            (
                BundleSealStage.OrphanAdoption,
                io,
                Skip,
                BundleSealFailureLog.Deferred,
                "orphan_file_unreadable"
            ),
            (
                BundleSealStage.OrphanAdoption,
                codec,
                Degrade,
                BundleSealFailureLog.Degraded,
                "orphan_file_invalid"
            ),
            (
                BundleSealStage.FileRecovery,
                io,
                Skip,
                BundleSealFailureLog.MaintenanceFailed,
                "file_recovery_failed"
            ),
            (
                BundleSealStage.JobDiscovery,
                missing,
                Skip,
                BundleSealFailureLog.EnvironmentBlocked,
                "job_discovery_failed"
            ),
        };
        foreach (var (stage, exception, disposition, log, code) in cases)
        {
            var name = $"{stage}/{exception?.GetType().Name ?? "observed"}";
            var decision = BundleSealFailurePolicy.Decide(stage, exception, fresh);
            Equal(disposition, decision.Disposition, name + " disposition");
            Equal(log, decision.Log, name + " log");
            Equal(code, decision.Code, name + " code");
        }

        var wrapped = BundleSealFailurePolicy.Decide(
            BundleSealStage.Build,
            new InvalidOperationException("wrapper", missing),
            fresh
        );
        Equal(
            "System.MissingMethodException: Newtonsoft.Json.Linq.JToken.ToString",
            wrapped.Detail,
            "detail names the environment link, not its wrapper"
        );
        var longMessage = BundleSealFailurePolicy.Decide(
            BundleSealStage.Build,
            new InvalidDataException(new string('x', 4096)),
            fresh
        );
        Equal(512, longMessage.Detail!.Length, "persisted diagnostic stays bounded");
    }

    private static void BoundsRetriesByAttemptsAndAge()
    {
        var io = new IOException("locked");
        Expect(
            BundleSealStage.Attempt,
            io,
            new("seal_attempt_failed", 7, 15 * Day),
            Terminal,
            "seal_retry_exhausted",
            8,
            "many failures of an old job exhaust"
        );
        Expect(
            BundleSealStage.Attempt,
            io,
            new("seal_attempt_failed", 6, 15 * Day),
            Retry,
            "seal_attempt_failed",
            7,
            "one attempt short keeps retrying"
        );
        Expect(
            BundleSealStage.Attempt,
            io,
            new("seal_attempt_failed", 7, 13 * Day),
            Retry,
            "seal_attempt_failed",
            8,
            "a young job keeps retrying"
        );
        Expect(
            BundleSealStage.Composition,
            new InvalidOperationException(),
            new(null, 1, 40 * Day),
            Retry,
            "payload_compose_failed",
            1,
            "a revived legacy job's first failure never exhausts"
        );
        Expect(
            BundleSealStage.Publish,
            io,
            new("payload_compose_failed", 7, 15 * Day),
            Retry,
            "seal_publish_failed",
            1,
            "a different failure restarts the consecutive count"
        );
        Expect(
            BundleSealStage.ReplayLoad,
            io,
            new("replay_unreadable", 7, 15 * Day),
            Degrade,
            "replay_unreadable",
            8,
            "an exhausted replay read omits that replay instead of losing the Run"
        );
    }

    private static void ExpiresParkedJobsOnlyAfterLaunchesAndAge()
    {
        var missing = new MissingMethodException();
        Expect(
            BundleSealStage.Build,
            missing,
            new(Blocked, 4, 61 * Day),
            Terminal,
            BundleSealFailurePolicy.EnvironmentBlockedExpiredCode,
            5,
            "parked across five launches and sixty days expires"
        );
        Expect(
            BundleSealStage.Build,
            missing,
            new(Blocked, 3, 61 * Day),
            Park,
            Blocked,
            4,
            "fewer launches stay parked"
        );
        Expect(
            BundleSealStage.Build,
            missing,
            new(Blocked, 10, 59 * Day),
            Park,
            Blocked,
            11,
            "younger than sixty days stays parked"
        );
        Expect(
            BundleSealStage.Build,
            missing,
            new("seal_publish_failed", 9, 61 * Day),
            Park,
            Blocked,
            1,
            "transient attempts do not count as parked launches"
        );
    }

    private static void SchedulesDueJobs()
    {
        var cases = new (
            string? Code,
            int Attempts,
            float? SinceAttempt,
            float SinceLaunch,
            bool Due,
            string Name
        )[]
        {
            (null, 0, null, 0f, true, "never failed"),
            (null, 1, 1f, 0f, true, "legacy allocation count without a failure"),
            (Blocked, 2, 100f, 50f, true, "parked in an earlier launch"),
            (Blocked, 2, 10f, 50f, false, "parked in this launch"),
            (Blocked, 2, -5f, 50f, true, "clock moved backwards"),
            ("seal_publish_failed", 1, 4.9f, 0f, false, "first backoff"),
            ("seal_publish_failed", 1, 5f, 0f, true, "first backoff elapsed"),
            ("seal_publish_failed", 3, 19f, 0f, false, "third backoff"),
            ("seal_publish_failed", 3, 20f, 0f, true, "third backoff elapsed"),
            ("seal_publish_failed", 40, 3599f, 0f, false, "backoff cap"),
            ("seal_publish_failed", 40, 3600f, 0f, true, "backoff cap elapsed"),
            ("seal_publish_failed", 0, 0f, 0f, true, "no recorded attempts"),
        };
        foreach (var (code, attempts, sinceAttempt, sinceLaunch, due, name) in cases)
            Equal(
                due,
                BundleSealFailurePolicy.IsDue(code, attempts, sinceAttempt, sinceLaunch),
                "due " + name
            );
    }

    private static void Expect(
        BundleSealStage stage,
        Exception exception,
        BundleSealRetryFacts retry,
        BundleSealFailureDisposition disposition,
        string code,
        int attempts,
        string name
    )
    {
        var decision = BundleSealFailurePolicy.Decide(stage, exception, retry);
        Equal(disposition, decision.Disposition, name + " disposition");
        Equal(code, decision.Code, name + " code");
        Equal(attempts, decision.Attempts, name + " attempts");
    }

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

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }

    private sealed class FakeDbException : DbException { }
}
