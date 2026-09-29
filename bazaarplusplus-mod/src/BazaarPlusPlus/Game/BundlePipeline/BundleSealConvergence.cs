#nullable enable
namespace BazaarPlusPlus.Game.BundlePipeline;

internal enum BundleSealInputGate
{
    ReplayPersistence,
    PlayerAccount,
    Screenshot,
    ReplayPayload,
    EncodedPayload,
}

internal enum BundleSealConvergenceDecision
{
    Continue,
    Wait,
    MarkScreenshotTimedOutAndContinue,
    MarkTerminal,
}

internal readonly struct BundleSealJobFacts
{
    internal BundleSealJobFacts(bool screenshotRequested, bool screenshotUnavailable)
    {
        ScreenshotRequested = screenshotRequested;
        ScreenshotUnavailable = screenshotUnavailable;
    }

    internal bool ScreenshotRequested { get; }
    internal bool ScreenshotUnavailable { get; }
}

internal readonly struct BundleSealInputObservation
{
    private BundleSealInputObservation(
        BundleSealInputGate gate,
        bool inputAvailable,
        int replayOmittedCount,
        bool encodedPayloadTooLarge
    )
    {
        Gate = gate;
        InputAvailable = inputAvailable;
        ReplayOmittedCount = replayOmittedCount;
        EncodedPayloadTooLarge = encodedPayloadTooLarge;
    }

    internal BundleSealInputGate Gate { get; }
    internal bool InputAvailable { get; }
    internal int ReplayOmittedCount { get; }
    internal bool EncodedPayloadTooLarge { get; }

    internal static BundleSealInputObservation Availability(
        BundleSealInputGate gate,
        bool available
    ) => new(gate, available, 0, false);

    internal static BundleSealInputObservation ReplayPayload(int omittedCount) =>
        new(BundleSealInputGate.ReplayPayload, omittedCount == 0, omittedCount, false);

    internal static BundleSealInputObservation EncodedPayload(bool tooLarge) =>
        new(BundleSealInputGate.EncodedPayload, !tooLarge, 0, tooLarge);
}

internal static class BundleSealConvergence
{
    internal static BundleSealConvergenceDecision Resolve(
        BundleSealJobFacts job,
        float secondsUntilInputDeadline,
        BundleSealInputObservation observation
    )
    {
        var deadlineReached = secondsUntilInputDeadline <= 0f;
        switch (observation.Gate)
        {
            case BundleSealInputGate.ReplayPersistence:
                return !observation.InputAvailable && !deadlineReached
                    ? BundleSealConvergenceDecision.Wait
                    : BundleSealConvergenceDecision.Continue;
            case BundleSealInputGate.PlayerAccount:
                if (observation.InputAvailable)
                    return BundleSealConvergenceDecision.Continue;
                return deadlineReached
                    ? BundleSealConvergenceDecision.MarkTerminal
                    : BundleSealConvergenceDecision.Wait;
            case BundleSealInputGate.Screenshot:
                if (
                    !job.ScreenshotRequested
                    || job.ScreenshotUnavailable
                    || observation.InputAvailable
                )
                    return BundleSealConvergenceDecision.Continue;
                return deadlineReached
                    ? BundleSealConvergenceDecision.MarkScreenshotTimedOutAndContinue
                    : BundleSealConvergenceDecision.Wait;
            case BundleSealInputGate.ReplayPayload:
                return observation.ReplayOmittedCount > 0 && !deadlineReached
                    ? BundleSealConvergenceDecision.Wait
                    : BundleSealConvergenceDecision.Continue;
            case BundleSealInputGate.EncodedPayload:
                return observation.EncodedPayloadTooLarge
                    ? BundleSealConvergenceDecision.MarkTerminal
                    : BundleSealConvergenceDecision.Continue;
            default:
                return BundleSealConvergenceDecision.MarkTerminal;
        }
    }
}

// Failure half of the seal decision core: what a thrown or observed failure means for durable
// queue state. Input deadlines stay in BundleSealConvergence.Resolve; this type never waits on
// inputs, and Resolve never inspects exceptions.
internal enum BundleSealStage
{
    AccountResolution,
    Screenshot,
    Composition,
    ReplayLoad,
    PayloadEncode,
    Allocation,
    Build,
    Publish,
    Attempt,
    PendingValidation,
    OrphanAdoption,
    FileRecovery,
    JobDiscovery,
}

internal enum BundleSealFailureCause
{
    Environment,
    Transient,
    Other,
}

internal enum BundleSealFailureDisposition
{
    RetryLater,
    ParkUntilNextLaunch,
    Terminal,
    Reseal,
    Degrade,
    Skip,
}

internal enum BundleSealFailureLog
{
    Deferred,
    EnvironmentBlocked,
    Degraded,
    Terminal,
    MaintenanceFailed,
}

internal readonly struct BundleSealRetryFacts
{
    internal BundleSealRetryFacts(
        string? lastErrorCode,
        int attempts,
        float secondsSinceInputDeadline
    )
    {
        LastErrorCode = lastErrorCode;
        Attempts = attempts;
        SecondsSinceInputDeadline = secondsSinceInputDeadline;
    }

    internal string? LastErrorCode { get; }
    internal int Attempts { get; }
    internal float SecondsSinceInputDeadline { get; }
}

internal readonly struct BundleSealFailureDecision
{
    internal BundleSealFailureDecision(
        BundleSealFailureDisposition disposition,
        BundleSealFailureCause cause,
        BundleSealFailureLog log,
        string code,
        string? detail,
        int attempts
    )
    {
        Disposition = disposition;
        Cause = cause;
        Log = log;
        Code = code;
        Detail = detail;
        Attempts = attempts;
    }

    internal BundleSealFailureDisposition Disposition { get; }
    internal BundleSealFailureCause Cause { get; }
    internal BundleSealFailureLog Log { get; }

    // Bounded, stable: persisted as last_error_code and logged as the public category.
    internal string Code { get; }

    // Bounded diagnostic persisted as last_error_detail; the full exception goes to the log.
    internal string? Detail { get; }

    // Consecutive failures with this Code, including this one.
    internal int Attempts { get; }
}

internal static class BundleSealFailurePolicy
{
    internal const string EnvironmentBlockedCode = "seal_environment_blocked";
    internal const string EnvironmentBlockedExpiredCode = "seal_environment_blocked_expired";
    internal const string RetryExhaustedCode = "seal_retry_exhausted";
    internal const int RetryAttemptsBeforeExhaustion = 8;
    internal const float RetryExhaustionSeconds = 14f * 24f * 3600f;
    internal const int ParkedLaunchesBeforeExpiry = 5;
    internal const float ParkedExpirySeconds = 60f * 24f * 3600f;
    internal const float InitialBackoffSeconds = 5f;
    internal const float MaximumBackoffSeconds = 3600f;
    private const int MaximumDetailLength = 512;
    private const int MaximumVisitedExceptions = 64;

    // The runtime could not bind or initialize code: the same process cannot succeed by retrying.
    internal static BundleSealFailureCause Classify(Exception? exception) =>
        Classify(exception, out _);

    internal static BundleSealFailureCause Classify(Exception? exception, out Exception? cause)
    {
        cause = exception;
        if (exception == null)
            return BundleSealFailureCause.Other;
        Exception? transient = null;
        var pending = new Stack<Exception>();
        pending.Push(exception);
        // Exception graphs are trees in practice; the visit budget only guards a malformed cycle.
        for (var visits = 0; pending.Count > 0 && visits < MaximumVisitedExceptions; visits++)
        {
            var link = pending.Pop();
            if (IsEnvironment(link))
            {
                cause = link;
                return BundleSealFailureCause.Environment;
            }
            if (transient == null && IsTransient(link))
                transient = link;
            if (link is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                    pending.Push(aggregate.InnerExceptions[index]);
            }
            else if (link.InnerException != null)
                pending.Push(link.InnerException);
        }
        if (transient == null)
            return BundleSealFailureCause.Other;
        cause = transient;
        return BundleSealFailureCause.Transient;
    }

    internal static BundleSealFailureDecision Decide(
        BundleSealStage stage,
        Exception? exception,
        BundleSealRetryFacts retry
    )
    {
        var cause = Classify(exception, out var link);
        var detail = Describe(link);
        switch (stage)
        {
            case BundleSealStage.PendingValidation:
                return cause switch
                {
                    BundleSealFailureCause.Environment => Maintenance(
                        BundleSealFailureDisposition.Skip,
                        cause,
                        BundleSealFailureLog.EnvironmentBlocked,
                        EnvironmentBlockedCode,
                        detail
                    ),
                    BundleSealFailureCause.Transient => Maintenance(
                        BundleSealFailureDisposition.Skip,
                        cause,
                        BundleSealFailureLog.Deferred,
                        "pending_file_unreadable",
                        detail
                    ),
                    _ => Maintenance(
                        BundleSealFailureDisposition.Reseal,
                        cause,
                        BundleSealFailureLog.Degraded,
                        "pending_file_invalid",
                        detail
                    ),
                };
            case BundleSealStage.OrphanAdoption:
                return cause switch
                {
                    BundleSealFailureCause.Environment => Maintenance(
                        BundleSealFailureDisposition.Skip,
                        cause,
                        BundleSealFailureLog.EnvironmentBlocked,
                        EnvironmentBlockedCode,
                        detail
                    ),
                    BundleSealFailureCause.Transient => Maintenance(
                        BundleSealFailureDisposition.Skip,
                        cause,
                        BundleSealFailureLog.Deferred,
                        "orphan_file_unreadable",
                        detail
                    ),
                    _ => Maintenance(
                        BundleSealFailureDisposition.Degrade,
                        cause,
                        BundleSealFailureLog.Degraded,
                        "orphan_file_invalid",
                        detail
                    ),
                };
            case BundleSealStage.FileRecovery:
            case BundleSealStage.JobDiscovery:
                return Maintenance(
                    BundleSealFailureDisposition.Skip,
                    cause,
                    cause == BundleSealFailureCause.Environment
                        ? BundleSealFailureLog.EnvironmentBlocked
                        : BundleSealFailureLog.MaintenanceFailed,
                    stage == BundleSealStage.FileRecovery
                        ? "file_recovery_failed"
                        : "job_discovery_failed",
                    detail
                );
        }

        if (cause == BundleSealFailureCause.Environment)
        {
            var parked = NextAttempts(retry, EnvironmentBlockedCode);
            return
                parked >= ParkedLaunchesBeforeExpiry
                && retry.SecondsSinceInputDeadline >= ParkedExpirySeconds
                ? new BundleSealFailureDecision(
                    BundleSealFailureDisposition.Terminal,
                    cause,
                    BundleSealFailureLog.Terminal,
                    EnvironmentBlockedExpiredCode,
                    detail,
                    parked
                )
                : new BundleSealFailureDecision(
                    BundleSealFailureDisposition.ParkUntilNextLaunch,
                    cause,
                    BundleSealFailureLog.EnvironmentBlocked,
                    EnvironmentBlockedCode,
                    detail,
                    parked
                );
        }

        var code = StageCode(stage, cause);
        if (cause == BundleSealFailureCause.Other)
        {
            // Codecs are deterministic over the same local inputs; retrying cannot change them.
            if (stage is BundleSealStage.Build or BundleSealStage.PayloadEncode)
                return Job(
                    BundleSealFailureDisposition.Terminal,
                    cause,
                    BundleSealFailureLog.Terminal,
                    code,
                    detail,
                    retry
                );
            if (stage == BundleSealStage.ReplayLoad)
                return Job(
                    BundleSealFailureDisposition.Degrade,
                    cause,
                    BundleSealFailureLog.Degraded,
                    code,
                    detail,
                    retry
                );
        }

        var attempts = NextAttempts(retry, code);
        var exhausted =
            attempts >= RetryAttemptsBeforeExhaustion
            && retry.SecondsSinceInputDeadline >= RetryExhaustionSeconds;
        if (!exhausted)
            return new BundleSealFailureDecision(
                BundleSealFailureDisposition.RetryLater,
                cause,
                BundleSealFailureLog.Deferred,
                code,
                detail,
                attempts
            );
        // An exhausted replay read degrades that one battle instead of losing the whole Run.
        return stage == BundleSealStage.ReplayLoad
            ? Job(
                BundleSealFailureDisposition.Degrade,
                cause,
                BundleSealFailureLog.Degraded,
                code,
                detail,
                retry
            )
            : new BundleSealFailureDecision(
                BundleSealFailureDisposition.Terminal,
                cause,
                BundleSealFailureLog.Terminal,
                RetryExhaustedCode,
                detail,
                attempts
            );
    }

    // Whether a waiting job may run now. Parked jobs run once per launch; retrying jobs back off
    // exponentially from their last recorded failure.
    internal static bool IsDue(
        string? lastErrorCode,
        int attempts,
        float? secondsSinceLastAttempt,
        float secondsSinceLaunch
    )
    {
        if (lastErrorCode == null || attempts <= 0 || secondsSinceLastAttempt is not { } elapsed)
            return true;
        if (elapsed < 0f)
            return true;
        if (lastErrorCode == EnvironmentBlockedCode)
            return elapsed > secondsSinceLaunch;
        return elapsed >= BackoffSeconds(attempts);
    }

    internal static float BackoffSeconds(int attempts)
    {
        if (attempts <= 0)
            return 0f;
        var exponent = Math.Min(attempts - 1, 30);
        return Math.Min(
            InitialBackoffSeconds * (float)Math.Pow(2, exponent),
            MaximumBackoffSeconds
        );
    }

    private static bool IsEnvironment(Exception exception) =>
        exception
            is MissingMemberException
                or TypeLoadException
                or BadImageFormatException
                or TypeInitializationException
                or EntryPointNotFoundException
                or InvalidProgramException
                or System.Security.VerificationException
        || exception is FileLoadException load && IsAssemblyIdentity(load.FileName)
        || exception is FileNotFoundException missing && IsAssemblyIdentity(missing.FileName);

    private static bool IsTransient(Exception exception) =>
        exception
            is IOException
                or UnauthorizedAccessException
                or TimeoutException
                or OperationCanceledException
                or System.Data.Common.DbException;

    // Assembly binding reports a display name ("Name, Version=..., PublicKeyToken=..."); file IO
    // reports a path. Only the former is an environment failure.
    private static bool IsAssemblyIdentity(string? fileName) =>
        fileName != null
        && (
            fileName.Contains("Version=", StringComparison.Ordinal)
            || fileName.Contains("PublicKeyToken=", StringComparison.Ordinal)
        );

    private static string StageCode(BundleSealStage stage, BundleSealFailureCause cause) =>
        stage switch
        {
            BundleSealStage.AccountResolution => "account_resolve_failed",
            BundleSealStage.Screenshot => "screenshot_encode_failed",
            BundleSealStage.Composition => "payload_compose_failed",
            BundleSealStage.ReplayLoad => cause == BundleSealFailureCause.Transient
                ? "replay_unreadable"
                : "replay_invalid",
            BundleSealStage.PayloadEncode => "payload_encode_failed",
            BundleSealStage.Allocation => "seal_allocation_failed",
            BundleSealStage.Build => "bundle_build_failed",
            BundleSealStage.Publish => "seal_publish_failed",
            _ => "seal_attempt_failed",
        };

    private static int NextAttempts(BundleSealRetryFacts retry, string code) =>
        (string.Equals(retry.LastErrorCode, code, StringComparison.Ordinal) ? retry.Attempts : 0)
        + 1;

    private static BundleSealFailureDecision Job(
        BundleSealFailureDisposition disposition,
        BundleSealFailureCause cause,
        BundleSealFailureLog log,
        string code,
        string? detail,
        BundleSealRetryFacts retry
    ) => new(disposition, cause, log, code, detail, NextAttempts(retry, code));

    private static BundleSealFailureDecision Maintenance(
        BundleSealFailureDisposition disposition,
        BundleSealFailureCause cause,
        BundleSealFailureLog log,
        string code,
        string? detail
    ) => new(disposition, cause, log, code, detail, 0);

    private static string? Describe(Exception? exception)
    {
        if (exception == null)
            return null;
        var text = exception.GetType().FullName + ": " + exception.Message;
        return text.Length <= MaximumDetailLength ? text : text.Substring(0, MaximumDetailLength);
    }
}
