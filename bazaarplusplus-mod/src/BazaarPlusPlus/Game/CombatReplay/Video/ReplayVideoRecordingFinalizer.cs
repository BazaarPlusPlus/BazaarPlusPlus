#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Audio;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// The off-main-thread half of a recording: every input arrives frozen in a context, and each
/// finalizer closes its operation exactly once. It has no Unity dependencies, so the recorder's
/// terminal outcomes are testable without a running game.
/// </summary>
internal static class ReplayVideoRecordingFinalizer
{
    internal static void CompleteEndedRecording(EndedRecordingFinalizeContext context)
    {
        var recording = context.Recording;
        ReplayVideoCaptureResult? result = null;
        try
        {
            result = recording.Drain.Complete();
            var inputs = ResolveEndedInputs(
                context.Start,
                recording.AudioStop.GetAwaiter().GetResult()
            );
            var mux = context.Muxer.Resolve(
                recording.Operation.RecordingId,
                result.Status,
                recording.TempVideoPath,
                recording.FinalPath,
                inputs.UsableWavPaths
            );
            try
            {
                var resolvedMetadata = TrySaveFinishMetadataFor(
                    context.Store,
                    context.VideoDir,
                    mux.FinalFilePath,
                    result,
                    mux.Status != ReplayVideoAudioMuxer.MuxStatus.Failed,
                    mux.FileSizeBytes,
                    inputs.MetadataStatus
                );
                recording.Operations.CompleteResolved(
                    recording.Operation,
                    result,
                    mux,
                    inputs.AudioStatus,
                    resolvedMetadata.Status,
                    inputs.DegradationReason,
                    inputs.DegradationException ?? resolvedMetadata.Exception
                );
            }
            catch (Exception ex)
            {
                recording.Operations.CompleteMuxCallbackFailure(
                    recording.Operation,
                    result,
                    mux,
                    inputs.AudioStatus,
                    inputs.MetadataStatus,
                    ex
                );
            }
        }
        catch (Exception ex)
        {
            CompleteFailedRecording(recording, result, ex);
        }
    }

    internal static void CompleteEndedFailure(
        RecordingFinalizeContext recording,
        Exception exception
    )
    {
        ReplayVideoCaptureResult? result = null;
        try
        {
            result = recording.Drain.Complete();
        }
        catch (Exception drainException)
        {
            exception = new AggregateException(exception, drainException);
        }

        CompleteFailedRecording(recording, result, exception);
    }

    private static void CompleteFailedRecording(
        RecordingFinalizeContext recording,
        ReplayVideoCaptureResult? result,
        Exception exception
    )
    {
        // A WAV is still open until its tap has stopped.
        WaitForAudioStop(recording.AudioStop);
        DeleteTempFile(recording.TempVideoPath, recording.Operation.RecordingId);
        DeleteWavBestEffort(recording.WavPaths);
        recording.Operations.TryComplete(
            recording.Operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = recording.FinalPath,
                CapturedFrames = result?.CapturedFrames ?? 0,
                DroppedFrames = result?.DroppedFrames ?? 0,
                AudioStatus = ReplayVideoAudioStatus.Failed,
                MetadataStatus = ReplayVideoMetadataStatus.Failed,
                ReasonCode =
                    result?.ReasonCode is { } resultReason
                    && resultReason != ReplayVideoRecordingReasonCode.Completed
                        ? resultReason
                        : ReplayVideoRecordingReasonCode.CaptureFailed,
                ExitCode = result?.ExitCode,
                StderrTail = result?.StderrTail,
                Exception = exception,
                EndedAtUtc = result?.EndedAtUtc,
            }
        );
    }

    // Off the main thread only: the start-metadata write and the tap stop are short, bounded
    // background tasks; the precedence below is the order the main thread used to apply them in
    // (audio start, then start metadata, then audio stop).
    private static EndedRecordingInputs ResolveEndedInputs(
        RecordingStartInputs start,
        ReplayAudioStopOutcome audio
    )
    {
        var metadata = start.Metadata.GetAwaiter().GetResult();
        var degradationReason = start.DegradationReason;
        var degradationException = start.DegradationException;
        if (metadata.Status != ReplayVideoMetadataStatus.Complete)
        {
            degradationReason ??= ReplayVideoRecordingReasonCode.MetadataFailed;
            degradationException ??= metadata.Exception;
        }

        var audioStatus = ResolveAudioStatus(audio, out var audioFailure);
        if (audioFailure.HasValue)
        {
            degradationReason ??= ReplayVideoRecordingReasonCode.AudioStopFailed;
            degradationException ??= audioFailure.Value.FailureException;
        }
        else if (audioStatus != ReplayVideoAudioStatus.Full)
        {
            degradationReason ??= ReplayVideoRecordingReasonCode.AudioUnavailable;
        }

        return new EndedRecordingInputs(
            audio.UsableWavPaths,
            audioStatus,
            metadata.Status,
            degradationReason,
            degradationException
        );
    }

    internal static ReplayVideoAudioStatus ResolveAudioStatus(
        ReplayAudioStopOutcome audio,
        out ReplayAudioCaptureResult? firstFailure
    )
    {
        firstFailure = null;
        foreach (var result in audio.Results)
        {
            if (result.FailureReason != ReplayAudioFailureReasonCode.None)
            {
                firstFailure = result;
                return ReplayVideoAudioStatus.Failed;
            }
        }

        return audio.UsableWavPaths.Count > 0
            ? ReplayVideoAudioStatus.Full
            : ReplayVideoAudioStatus.Silent;
    }

    internal static void WaitForAudioStop(Task<ReplayAudioStopOutcome>? audioStop)
    {
        try
        {
            audioStop?.Wait();
        }
        catch
        {
            // The stop already reported its own failure; cleanup only needs the WAVs closed.
        }
    }

    internal static void DeleteWavBestEffort(string? wavPath)
    {
        if (string.IsNullOrEmpty(wavPath))
            return;

        try
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
        catch
        {
            // best-effort
        }
    }

    internal static void DeleteWavBestEffort(IReadOnlyList<string>? wavPaths)
    {
        if (wavPaths == null)
            return;

        foreach (var wavPath in wavPaths)
            DeleteWavBestEffort(wavPath);
    }

    // The tap stop and the start-metadata write finished independently of the encoder, so they
    // are read first: a drain that throws must not erase their real outcomes.
    internal static void CompleteAbortedRecording(AbortedRecordingFinalizeContext context)
    {
        var recording = context.Recording;
        var terminalReason = context.TerminalReason;
        var terminalException = context.TerminalException;
        var audioStatus = ReplayVideoAudioStatus.Failed;
        var metadataStatus = ReplayVideoMetadataStatus.Failed;
        ReplayVideoCaptureResult? result = null;
        var finalResolutionSucceeded = false;
        try
        {
            audioStatus = ResolveAudioStatus(recording.AudioStop.GetAwaiter().GetResult(), out _);
            var startMetadata = context.StartMetadata.GetAwaiter().GetResult();
            metadataStatus = startMetadata.Status;
            if (metadataStatus != ReplayVideoMetadataStatus.Complete)
                terminalException ??= startMetadata.Exception;

            result = recording.Drain.Complete();
            if (result.Status == ReplayVideoCaptureStatus.Completed)
            {
                try
                {
                    ReplayVideoAudioMuxer.PromoteSilentToFinal(
                        recording.TempVideoPath,
                        recording.FinalPath
                    );
                    finalResolutionSucceeded = true;
                }
                catch (Exception ex)
                {
                    terminalReason = ReplayVideoRecordingReasonCode.PromotionFailed;
                    terminalException = ex;
                }
            }
            else if (!string.Equals(context.EndReason, "begin-exception", StringComparison.Ordinal))
            {
                terminalReason = result.ReasonCode;
            }

            var finalFileSize = ReplayVideoFileHelpers.TryGetFileSize(recording.FinalPath);
            var metadataOutcome = TrySaveFinishMetadataFor(
                context.Store,
                context.VideoDir,
                recording.FinalPath,
                result,
                finalResolutionSucceeded,
                finalFileSize,
                metadataStatus
            );
            metadataStatus = metadataOutcome.Status;
            terminalException ??= metadataOutcome.Exception;
        }
        catch (Exception ex)
        {
            terminalReason = ReplayVideoRecordingReasonCode.CaptureFailed;
            terminalException = ex;
        }

        WaitForAudioStop(recording.AudioStop);
        DeleteTempFile(recording.TempVideoPath, recording.Operation.RecordingId);
        DeleteWavBestEffort(recording.WavPaths);
        recording.Operations.TryComplete(
            recording.Operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = recording.FinalPath,
                CapturedFrames = result?.CapturedFrames ?? 0,
                DroppedFrames = result?.DroppedFrames ?? 0,
                AudioStatus = audioStatus,
                MetadataStatus = metadataStatus,
                ReasonCode = terminalReason,
                ExitCode = result?.ExitCode,
                StderrTail = result?.StderrTail,
                Exception = terminalException ?? result?.Exception,
                EndedAtUtc = result?.EndedAtUtc,
            }
        );
    }

    // An abort whose session never produced a drain has no video, but its tap stop and start
    // metadata still report their own outcomes.
    internal static void CompleteAbortedWithoutDrain(
        ReplayVideoRecordingLifecycle operations,
        ReplayVideoRecordingOperation operation,
        string tempPath,
        string finalPath,
        IReadOnlyList<string>? wavPaths,
        Task<ReplayAudioStopOutcome> audioStop,
        Task<MetadataWriteOutcome> startMetadata,
        ReplayVideoRecordingReasonCode terminalReason,
        Exception? terminalException
    )
    {
        var audioStatus = ResolveAudioStatus(audioStop.GetAwaiter().GetResult(), out _);
        var metadata = startMetadata.GetAwaiter().GetResult();
        DeleteTempFile(tempPath, operation.RecordingId);
        DeleteWavBestEffort(wavPaths);
        operations.TryComplete(
            operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = finalPath,
                AudioStatus = audioStatus,
                MetadataStatus = metadata.Status,
                ReasonCode = terminalReason,
                Exception =
                    terminalException
                    ?? (
                        metadata.Status == ReplayVideoMetadataStatus.Complete
                            ? null
                            : metadata.Exception
                    ),
            }
        );
    }

    // A start that throws after its operation became active hands that operation to the abort's
    // finalizer, which owns its terminal outcome; only an operation the abort never took closes
    // here.
    internal static void CompleteBeginException(
        ReplayVideoRecordingLifecycle operations,
        ReplayVideoRecordingOperation operation,
        ReplayVideoRecordingOperation? abortedOperation,
        Exception exception
    )
    {
        if (ReferenceEquals(abortedOperation, operation))
            return;

        operations.TryComplete(
            operation,
            new ReplayVideoRecordingCompletion
            {
                ReasonCode = ReplayVideoRecordingReasonCode.BeginException,
                AudioStatus = ReplayVideoAudioStatus.Failed,
                MetadataStatus = ReplayVideoMetadataStatus.Unavailable,
                Exception = exception,
            }
        );
    }

    // Parameterized so it is race-free under the async mux: every input is a value
    // captured on the main thread before instance fields are nulled. Safe to call on
    // a background thread because the store opens a fresh SQLite connection per call.
    // FileSizeBytes always comes from the resolved final path, never the first-pass temp.
    private static MetadataWriteOutcome TrySaveFinishMetadataFor(
        CombatReplayVideoMetadataStore? store,
        string? videoDir,
        string finalPath,
        ReplayVideoCaptureResult result,
        bool finalResolutionSucceeded,
        long finalFileSize,
        ReplayVideoMetadataStatus initialStatus
    )
    {
        if (store == null)
            return new MetadataWriteOutcome(initialStatus, null);

        try
        {
            var relativePath = ComputeRelativePath(videoDir, finalPath);
            var endedAt = result.EndedAtUtc ?? DateTimeOffset.UtcNow;
            var status = ReplayVideoMetadataResolution.ResolvePersistedStatus(
                result.Status,
                finalResolutionSucceeded,
                finalFileSize
            );

            store.SaveFinish(
                new VideoRecordingFinished
                {
                    VideoId = result.VideoId,
                    VideoRelativePath = relativePath,
                    EndedAtUtc = endedAt,
                    DurationMs = result.VideoDurationMs,
                    CapturedFrames = result.CapturedFrames,
                    DroppedFrames = result.DroppedFrames,
                    FileSizeBytes = finalFileSize,
                    Status = status,
                    Error = result.Error,
                }
            );
            return new MetadataWriteOutcome(
                initialStatus == ReplayVideoMetadataStatus.Complete
                    ? ReplayVideoMetadataStatus.Complete
                    : ReplayVideoMetadataStatus.Failed,
                null
            );
        }
        catch (Exception ex)
        {
            return new MetadataWriteOutcome(ReplayVideoMetadataStatus.Failed, ex);
        }
    }

    internal static string ComputeRelativePath(string? rootDirectory, string filePath)
    {
        return ReplayVideoManagedPath.TryMakeRelative(
            rootDirectory ?? string.Empty,
            filePath,
            out var relativePath
        )
            ? relativePath
            : string.Empty;
    }

    internal static void DeleteTempFile(string? tempPath, string? recordingId)
    {
        if (string.IsNullOrWhiteSpace(tempPath))
            return;

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.video_recording.cleanup_failed"
                ),
                ex,
                () =>
                    [
                        ("recording_id", recordingId, BppLogCorrelationPolicy.Short),
                        ("stage", ReplayVideoLogStage.TempDelete),
                        ("path", tempPath),
                    ]
            );
        }
    }
}

internal readonly struct MetadataWriteOutcome
{
    internal static MetadataWriteOutcome Complete => new(ReplayVideoMetadataStatus.Complete, null);

    internal static MetadataWriteOutcome Unavailable =>
        new(ReplayVideoMetadataStatus.Unavailable, null);

    internal MetadataWriteOutcome(ReplayVideoMetadataStatus status, Exception? exception)
    {
        Status = status;
        Exception = exception;
    }

    internal ReplayVideoMetadataStatus Status { get; }
    internal Exception? Exception { get; }
}

internal sealed record RecordingFinalizeContext(
    ReplayVideoRecordingLifecycle Operations,
    ReplayVideoRecordingOperation Operation,
    ReplayVideoEncoderDrain Drain,
    string TempVideoPath,
    string FinalPath,
    IReadOnlyList<string>? WavPaths,
    Task<ReplayAudioStopOutcome> AudioStop
);

internal sealed record RecordingStartInputs(
    Task<MetadataWriteOutcome> Metadata,
    ReplayVideoRecordingReasonCode? DegradationReason,
    Exception? DegradationException
);

internal sealed record EndedRecordingInputs(
    IReadOnlyList<string> UsableWavPaths,
    ReplayVideoAudioStatus AudioStatus,
    ReplayVideoMetadataStatus MetadataStatus,
    ReplayVideoRecordingReasonCode? DegradationReason,
    Exception? DegradationException
);

internal sealed record EndedRecordingFinalizeContext(
    RecordingFinalizeContext Recording,
    ReplayVideoAudioMuxer Muxer,
    CombatReplayVideoMetadataStore? Store,
    string? VideoDir,
    RecordingStartInputs Start
);

internal sealed record AbortedRecordingFinalizeContext(
    RecordingFinalizeContext Recording,
    string EndReason,
    CombatReplayVideoMetadataStore? Store,
    string? VideoDir,
    Task<MetadataWriteOutcome> StartMetadata,
    ReplayVideoRecordingReasonCode TerminalReason,
    Exception? TerminalException
);
