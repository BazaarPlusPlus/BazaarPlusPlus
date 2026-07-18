#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum ReplayVideoAudioStatus
{
    Full,
    Silent,
    Failed,
}

internal enum ReplayVideoMetadataStatus
{
    Complete,
    Unavailable,
    Failed,
}

internal enum ReplayVideoRecordingReasonCode
{
    Completed,
    ArtifactUnavailable,
    AudioUnavailable,
    AudioCaptureFailed,
    AudioStopFailed,
    CaptureFailed,
    EncoderStartFailed,
    EncoderWriterFailed,
    EncoderTimeout,
    EncoderNonZeroExit,
    MuxFallback,
    PromotionFailed,
    MetadataFailed,
    Superseded,
    Aborted,
    ShutdownTimeout,
    FfmpegUnavailable,
    AsyncGpuReadbackUnavailable,
    OutputPathUnavailable,
    InvalidDimensions,
    BeginException,
}

internal sealed class ReplayVideoRecordingCompletion
{
    internal string FinalFilePath { get; set; } = string.Empty;
    internal int CapturedFrames { get; set; }
    internal int DroppedFrames { get; set; }
    internal ReplayVideoAudioStatus AudioStatus { get; set; }
    internal ReplayVideoMetadataStatus MetadataStatus { get; set; }
    internal ReplayVideoRecordingReasonCode ReasonCode { get; set; }
    internal int? ExitCode { get; set; }
    internal string? StderrTail { get; set; }
    internal Exception? Exception { get; set; }
    internal string? Reason { get; set; }
    internal DateTimeOffset? EndedAtUtc { get; set; }
}

internal readonly record struct ReplayVideoRecordingTerminal(
    string RecordingId,
    string BattleId,
    CombatReplayPlaybackSource Source,
    string FinalFilePath,
    bool ArtifactUsable,
    ReplayVideoAudioStatus AudioStatus,
    ReplayVideoMetadataStatus MetadataStatus,
    ReplayVideoRecordingReasonCode ReasonCode,
    string? Reason
);

internal sealed class ReplayVideoRecordingOperation
{
    private readonly DateTimeOffset _startedAtUtc;
    private int _completed;

    internal ReplayVideoRecordingOperation(
        string recordingId,
        string battleId,
        CombatReplayPlaybackSource source,
        DateTimeOffset startedAtUtc
    )
    {
        RecordingId = recordingId ?? throw new ArgumentNullException(nameof(recordingId));
        BattleId = battleId ?? throw new ArgumentNullException(nameof(battleId));
        Source = source;
        _startedAtUtc = startedAtUtc;
    }

    internal string RecordingId { get; }
    internal string BattleId { get; }
    internal CombatReplayPlaybackSource Source { get; }
    internal bool IsCompleted => Volatile.Read(ref _completed) != 0;

    internal bool TryComplete(ReplayVideoRecordingCompletion completion)
    {
        return TryComplete(completion, out _);
    }

    internal bool TryComplete(
        ReplayVideoRecordingCompletion completion,
        out ReplayVideoRecordingTerminal terminal
    )
    {
        terminal = default;
        if (completion == null)
            throw new ArgumentNullException(nameof(completion));
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            return false;

        var fileSize = TryGetVerifiedFileSize(completion.FinalFilePath);
        var artifactUsable = fileSize > 0;
        var reason =
            !artifactUsable && completion.ReasonCode == ReplayVideoRecordingReasonCode.Completed
                ? ReplayVideoRecordingReasonCode.ArtifactUnavailable
                : completion.ReasonCode;
        var duration = Math.Max(
            0,
            (long)
                ((completion.EndedAtUtc ?? DateTimeOffset.UtcNow) - _startedAtUtc).TotalMilliseconds
        );
        var common = CommonFields(completion, reason, duration, fileSize);
        terminal = new ReplayVideoRecordingTerminal(
            RecordingId,
            BattleId,
            Source,
            completion.FinalFilePath,
            artifactUsable,
            completion.AudioStatus,
            completion.MetadataStatus,
            reason,
            completion.Reason ?? completion.Exception?.Message
        );

        if (!artifactUsable)
        {
            var failed = new List<Infrastructure.Logging.BppLogFieldValue>(common)
            {
                CombatReplayVideoLogEvents.ExitCode.Bind(completion.ExitCode),
                CombatReplayVideoLogEvents.StderrTail.Bind(completion.StderrTail),
            };
            if (completion.Exception == null)
                BppLog.ErrorEvent(CombatReplayVideoLogEvents.RecordingFailed, failed.ToArray());
            else
            {
                BppLog.ErrorEvent(
                    CombatReplayVideoLogEvents.RecordingFailed,
                    completion.Exception,
                    failed.ToArray()
                );
            }
            return true;
        }

        if (
            reason == ReplayVideoRecordingReasonCode.Completed
            && completion.AudioStatus == ReplayVideoAudioStatus.Full
            && completion.MetadataStatus == ReplayVideoMetadataStatus.Complete
        )
        {
            BppLog.InfoEvent(CombatReplayVideoLogEvents.RecordingSucceeded, common);
            return true;
        }

        if (completion.Exception == null)
            BppLog.WarnEvent(CombatReplayVideoLogEvents.RecordingDegraded, common);
        else
        {
            BppLog.WarnEvent(
                CombatReplayVideoLogEvents.RecordingDegraded,
                completion.Exception,
                common
            );
        }
        return true;
    }

    private Infrastructure.Logging.BppLogFieldValue[] CommonFields(
        ReplayVideoRecordingCompletion completion,
        ReplayVideoRecordingReasonCode reason,
        long duration,
        long fileSize
    ) =>
        [
            CombatReplayVideoLogEvents.RecordingId.Bind(RecordingId),
            CombatReplayVideoLogEvents.BattleId.Bind(BattleId),
            CombatReplayVideoLogEvents.Source.Bind(Source),
            CombatReplayVideoLogEvents.ReasonCode.Bind(reason),
            CombatReplayVideoLogEvents.DurationMs.Bind(duration),
            CombatReplayVideoLogEvents.CapturedFrames.Bind(completion.CapturedFrames),
            CombatReplayVideoLogEvents.DroppedFrames.Bind(completion.DroppedFrames),
            CombatReplayVideoLogEvents.SizeBytes.Bind(fileSize),
            CombatReplayVideoLogEvents.AudioStatus.Bind(completion.AudioStatus),
            CombatReplayVideoLogEvents.MetadataStatus.Bind(completion.MetadataStatus),
            CombatReplayVideoLogEvents.OutputPath.Bind(completion.FinalFilePath),
        ];

    private static long TryGetVerifiedFileSize(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return 0;
        try
        {
            var file = new FileInfo(filePath);
            return file.Exists && file.Length > 0 ? file.Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}

internal sealed class ReplayVideoRecordingOperationRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ReplayVideoRecordingOperation> _pending = new(
        StringComparer.Ordinal
    );
    private readonly Action<ReplayVideoRecordingTerminal>? _completionObserver;

    internal ReplayVideoRecordingOperationRegistry()
        : this(null) { }

    internal ReplayVideoRecordingOperationRegistry(
        Action<ReplayVideoRecordingTerminal>? completionObserver
    )
    {
        _completionObserver = completionObserver;
    }

    internal int Count
    {
        get
        {
            lock (_sync)
                return _pending.Count;
        }
    }

    internal void Register(ReplayVideoRecordingOperation operation)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        lock (_sync)
            _pending.Add(operation.RecordingId, operation);
    }

    internal bool TryComplete(
        ReplayVideoRecordingOperation operation,
        ReplayVideoRecordingCompletion completion
    )
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        var completed = operation.TryComplete(completion, out var terminal);
        if (!completed)
            return false;
        lock (_sync)
            _pending.Remove(operation.RecordingId);
        try
        {
            _completionObserver?.Invoke(terminal);
        }
        catch
        {
            // A terminal observer is secondary to operation cleanup and must never strand an
            // operation in the registry.
        }
        return true;
    }

    internal void CompletePending(ReplayVideoRecordingReasonCode reasonCode)
    {
        ReplayVideoRecordingOperation[] pending;
        lock (_sync)
        {
            pending = new ReplayVideoRecordingOperation[_pending.Count];
            _pending.Values.CopyTo(pending, 0);
        }

        for (var index = 0; index < pending.Length; index++)
        {
            TryComplete(
                pending[index],
                new ReplayVideoRecordingCompletion
                {
                    ReasonCode = reasonCode,
                    AudioStatus = ReplayVideoAudioStatus.Failed,
                    MetadataStatus = ReplayVideoMetadataStatus.Unavailable,
                }
            );
        }
    }
}

internal sealed class ReplayVideoRecordingLifecycle
{
    private readonly ReplayVideoRecordingOperationRegistry _registry;
    private readonly Func<string> _recordingIdFactory;

    internal ReplayVideoRecordingLifecycle(Func<string>? recordingIdFactory = null)
        : this(recordingIdFactory, null) { }

    internal ReplayVideoRecordingLifecycle(
        Func<string>? recordingIdFactory,
        Action<ReplayVideoRecordingTerminal>? completionObserver
    )
    {
        _recordingIdFactory = recordingIdFactory ?? (() => Guid.NewGuid().ToString("N"));
        _registry = new ReplayVideoRecordingOperationRegistry(completionObserver);
    }

    internal int Count => _registry.Count;

    internal ReplayVideoRecordingOperation Start(
        string battleId,
        CombatReplayPlaybackSource source,
        DateTimeOffset startedAtUtc
    )
    {
        var operation = new ReplayVideoRecordingOperation(
            _recordingIdFactory(),
            battleId,
            source,
            startedAtUtc
        );
        _registry.Register(operation);
        return operation;
    }

    internal bool CompletePreflight(
        ReplayVideoRecordingOperation operation,
        ReplayVideoRecordingReasonCode reasonCode,
        Exception? exception = null,
        string? reason = null
    ) =>
        TryComplete(
            operation,
            new ReplayVideoRecordingCompletion
            {
                ReasonCode = reasonCode,
                AudioStatus = ReplayVideoAudioStatus.Silent,
                MetadataStatus = ReplayVideoMetadataStatus.Unavailable,
                Exception = exception,
                Reason = reason,
            }
        );

    internal bool CompleteResolved(
        ReplayVideoRecordingOperation operation,
        ReplayVideoCaptureResult capture,
        ReplayVideoAudioMuxer.MuxResult mux,
        ReplayVideoAudioStatus audioStatus,
        ReplayVideoMetadataStatus metadataStatus,
        ReplayVideoRecordingReasonCode? degradationReason,
        Exception? degradationException
    )
    {
        var reasonCode = ResolveTerminalReason(
            capture,
            mux,
            audioStatus,
            metadataStatus,
            degradationReason
        );
        return TryComplete(
            operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = mux.FinalFilePath,
                CapturedFrames = capture.CapturedFrames,
                DroppedFrames = capture.DroppedFrames,
                AudioStatus = audioStatus,
                MetadataStatus = metadataStatus,
                ReasonCode = reasonCode,
                ExitCode = capture.ExitCode ?? mux.ExitCode,
                StderrTail = !string.IsNullOrEmpty(capture.StderrTail)
                    ? capture.StderrTail
                    : mux.StderrTail,
                Exception = capture.Exception ?? mux.Exception ?? degradationException,
                EndedAtUtc = capture.EndedAtUtc,
            }
        );
    }

    internal bool CompleteMuxCallbackFailure(
        ReplayVideoRecordingOperation operation,
        ReplayVideoCaptureResult capture,
        ReplayVideoAudioMuxer.MuxResult mux,
        ReplayVideoAudioStatus audioStatus,
        ReplayVideoMetadataStatus metadataStatus,
        Exception exception
    ) =>
        TryComplete(
            operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = mux.FinalFilePath,
                CapturedFrames = capture.CapturedFrames,
                DroppedFrames = capture.DroppedFrames,
                AudioStatus = audioStatus,
                MetadataStatus = metadataStatus,
                ReasonCode = ReplayVideoRecordingReasonCode.MetadataFailed,
                ExitCode = capture.ExitCode ?? mux.ExitCode,
                StderrTail = !string.IsNullOrEmpty(capture.StderrTail)
                    ? capture.StderrTail
                    : mux.StderrTail,
                Exception = exception,
                EndedAtUtc = capture.EndedAtUtc,
            }
        );

    internal bool TryComplete(
        ReplayVideoRecordingOperation operation,
        ReplayVideoRecordingCompletion completion
    ) => _registry.TryComplete(operation, completion);

    internal void CompletePending(ReplayVideoRecordingReasonCode reasonCode) =>
        _registry.CompletePending(reasonCode);

    private static ReplayVideoRecordingReasonCode ResolveTerminalReason(
        ReplayVideoCaptureResult capture,
        ReplayVideoAudioMuxer.MuxResult mux,
        ReplayVideoAudioStatus audioStatus,
        ReplayVideoMetadataStatus metadataStatus,
        ReplayVideoRecordingReasonCode? degradationReason
    )
    {
        if (capture.Status != ReplayVideoCaptureStatus.Completed)
            return capture.ReasonCode;
        if (mux.Status == ReplayVideoAudioMuxer.MuxStatus.Failed)
            return ReplayVideoRecordingReasonCode.PromotionFailed;
        if (degradationReason.HasValue)
            return degradationReason.Value;
        if (capture.Degraded)
            return capture.ReasonCode;
        if (mux.Status == ReplayVideoAudioMuxer.MuxStatus.FellBackToSilent)
            return ReplayVideoRecordingReasonCode.MuxFallback;
        if (audioStatus == ReplayVideoAudioStatus.Failed)
            return ReplayVideoRecordingReasonCode.AudioStopFailed;
        if (audioStatus != ReplayVideoAudioStatus.Full)
            return ReplayVideoRecordingReasonCode.AudioUnavailable;
        if (metadataStatus != ReplayVideoMetadataStatus.Complete)
            return ReplayVideoRecordingReasonCode.MetadataFailed;
        return ReplayVideoRecordingReasonCode.Completed;
    }
}

internal static class ReplayVideoMetadataResolution
{
    internal static string ResolvePersistedStatus(
        ReplayVideoCaptureStatus captureStatus,
        bool finalResolutionSucceeded,
        long finalFileSize
    ) =>
        captureStatus == ReplayVideoCaptureStatus.Completed
        && finalResolutionSucceeded
        && finalFileSize > 0
            ? "COMPLETED"
            : "FAILED";
}
