#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoAudioMuxer
{
    private const int MuxTimeoutMs = 60_000;

    private static readonly object s_pendingLock = new();
    private static readonly HashSet<Task> s_pendingTasks = new();

    internal enum MuxStatus
    {
        Muxed,
        FellBackToSilent,
        Failed,
    }

    internal enum MuxReasonCode
    {
        Muxed,
        CaptureFailed,
        NoAudio,
        UnsupportedPlatform,
        NativeMuxUnavailable,
        NativeMuxTimeout,
        NativeMuxFailed,
        ZeroDurationOutput,
        PromotionFailed,
        UnexpectedException,
    }

    internal readonly struct MuxResult
    {
        public readonly MuxStatus Status;
        public readonly string FinalFilePath;
        public readonly long FileSizeBytes;
        public readonly MuxReasonCode ReasonCode;
        public readonly int? ExitCode;
        public readonly string StderrTail;
        public readonly Exception? Exception;

        public MuxResult(
            MuxStatus status,
            string finalFilePath,
            long fileSizeBytes,
            MuxReasonCode reasonCode,
            int? exitCode = null,
            string? stderrTail = null,
            Exception? exception = null
        )
        {
            Status = status;
            FinalFilePath = finalFilePath;
            FileSizeBytes = fileSizeBytes;
            ReasonCode = reasonCode;
            ExitCode = exitCode;
            StderrTail = stderrTail ?? string.Empty;
            Exception = exception;
        }
    }

    // Inline muxing keeps encoder drain and mux in one task tracked at shutdown.
    internal MuxResult Resolve(
        string recordingId,
        ReplayVideoCaptureStatus status,
        string tempVideoPath,
        string finalPath,
        IReadOnlyList<string> usableWavPaths
    )
    {
        try
        {
            var backend = ReplayVideoBackendPolicy.Current;
            if (
                TryResolveWithoutMux(
                    recordingId,
                    status,
                    tempVideoPath,
                    finalPath,
                    usableWavPaths,
                    out var existingWavPaths,
                    out var synchronous
                )
            )
            {
                return synchronous;
            }

            if (backend == ReplayVideoBackend.Unsupported)
                return FallBack(
                    tempVideoPath,
                    existingWavPaths,
                    finalPath,
                    MuxReasonCode.UnsupportedPlatform
                );

            return MuxNative(backend, recordingId, tempVideoPath, existingWavPaths, finalPath);
        }
        catch (Exception ex)
        {
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.UnexpectedException,
                exception: ex
            );
        }
    }

    private bool TryResolveWithoutMux(
        string recordingId,
        ReplayVideoCaptureStatus status,
        string tempVideoPath,
        string finalPath,
        IReadOnlyList<string> usableWavPaths,
        out IReadOnlyList<string> existingWavPaths,
        out MuxResult result
    )
    {
        existingWavPaths = Array.Empty<string>();
        if (status != ReplayVideoCaptureStatus.Completed)
        {
            result = DeleteTempAndReport(recordingId, tempVideoPath, usableWavPaths, finalPath);
            return true;
        }

        existingWavPaths = ReplayVideoFileHelpers.GetExistingWavPaths(usableWavPaths);
        if (existingWavPaths.Count == 0)
        {
            result = PromoteAndReport(
                tempVideoPath,
                usableWavPaths,
                finalPath,
                MuxStatus.FellBackToSilent,
                MuxReasonCode.NoAudio
            );
            return true;
        }

        result = default;
        return false;
    }

    private MuxResult MuxNative(
        ReplayVideoBackend backend,
        string recordingId,
        string silentVideoTempPath,
        IReadOnlyList<string> wavPaths,
        string finalPath,
        int audioBitrateKbps = 192
    )
    {
        try
        {
            var timeout = TimeSpan.FromMilliseconds(MuxTimeoutMs);
            var resultCode = 0;
            var error = string.Empty;
            var succeeded = false;
            var timedOut = false;
            if (backend == ReplayVideoBackend.WindowsNative)
            {
                var native = WindowsNativeReplayAudioMuxer.Mux(
                    silentVideoTempPath,
                    wavPaths,
                    finalPath,
                    audioBitrateKbps,
                    timeout
                );
                resultCode = native.ResultCode;
                error = native.Error;
                succeeded = native.Succeeded;
                timedOut = native.TimedOut;
            }
            else
            {
                var native = MacNativeReplayAudioMuxer.Mux(
                    silentVideoTempPath,
                    wavPaths,
                    finalPath,
                    audioBitrateKbps,
                    timeout
                );
                resultCode = native.ResultCode;
                error = native.Error;
                succeeded = native.Succeeded;
                timedOut = native.TimedOut;
            }
            if (succeeded && File.Exists(finalPath))
            {
                var mixedSize = ReplayVideoFileHelpers.TryGetFileSize(finalPath);
                var silentSize = ReplayVideoFileHelpers.TryGetFileSize(silentVideoTempPath);
                if (IsLikelyZeroDurationOutput(mixedSize, silentSize))
                {
                    return FallBack(
                        silentVideoTempPath,
                        wavPaths,
                        finalPath,
                        MuxReasonCode.ZeroDurationOutput,
                        exitCode: resultCode,
                        stderrTail: error
                    );
                }

                TryDelete(silentVideoTempPath);
                TryDelete(wavPaths);
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.video_mux.diagnostic_observed"
                    ),
                    () =>
                        [
                            ("recording_id", recordingId, BppLogCorrelationPolicy.Short),
                            ("stage", ReplayVideoLogStage.MuxCallback),
                            ("reason_code", MuxReasonCode.Muxed),
                            ("path", finalPath),
                            ("pending_count", PendingTaskCount),
                        ]
                );
                return new MuxResult(
                    MuxStatus.Muxed,
                    finalPath,
                    mixedSize,
                    MuxReasonCode.Muxed,
                    resultCode,
                    error
                );
            }

            var reason = timedOut
                ? MuxReasonCode.NativeMuxTimeout
                : (
                    resultCode == -10
                        ? MuxReasonCode.NativeMuxUnavailable
                        : MuxReasonCode.NativeMuxFailed
                );
            return FallBack(silentVideoTempPath, wavPaths, finalPath, reason, resultCode, error);
        }
        catch (Exception ex)
        {
            return FallBack(
                silentVideoTempPath,
                wavPaths,
                finalPath,
                MuxReasonCode.UnexpectedException,
                exception: ex
            );
        }
    }

    private MuxResult DeleteTempAndReport(
        string recordingId,
        string tempVideoPath,
        IReadOnlyList<string>? wavPaths,
        string finalPath
    )
    {
        try
        {
            if (File.Exists(tempVideoPath))
                File.Delete(tempVideoPath);
            TryDelete(wavPaths);
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                ReplayVideoFileHelpers.TryGetFileSize(finalPath),
                MuxReasonCode.CaptureFailed
            );
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
                        ("path", tempVideoPath),
                    ]
            );
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.CaptureFailed,
                exception: ex
            );
        }
    }

    private MuxResult PromoteAndReport(
        string silentVideoTempPath,
        IReadOnlyList<string>? wavPaths,
        string finalPath,
        MuxStatus status,
        MuxReasonCode reasonCode
    )
    {
        try
        {
            var size = PromoteSilentToFinal(silentVideoTempPath, finalPath);
            TryDelete(wavPaths);
            return new MuxResult(status, finalPath, size, reasonCode);
        }
        catch (Exception ex)
        {
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.PromotionFailed,
                exception: ex
            );
        }
    }

    private MuxResult FallBack(
        string silentVideoTempPath,
        IReadOnlyList<string>? wavPaths,
        string finalPath,
        MuxReasonCode reasonCode,
        int? exitCode = null,
        string? stderrTail = null,
        Exception? exception = null
    )
    {
        try
        {
            var size = PromoteSilentToFinal(silentVideoTempPath, finalPath);
            TryDelete(wavPaths);
            return new MuxResult(
                MuxStatus.FellBackToSilent,
                finalPath,
                size,
                reasonCode,
                exitCode,
                stderrTail,
                exception
            );
        }
        catch (Exception ex)
        {
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.PromotionFailed,
                exitCode,
                stderrTail,
                ex
            );
        }
    }

    // A valid mux includes the first-pass H.264 payload. Half its size allows container overhead differences while rejecting empty stubs.
    internal static bool IsLikelyZeroDurationOutput(long muxedSize, long silentSize)
    {
        if (muxedSize <= 0)
            return true;

        if (silentSize <= 0)
            return false;

        return muxedSize < silentSize / 2;
    }

    internal static long PromoteSilentToFinal(string tempPath, string finalPath)
    {
        if (!File.Exists(tempPath))
            return ReplayVideoFileHelpers.TryGetFileSize(finalPath);

        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(tempPath, finalPath);
        return ReplayVideoFileHelpers.TryGetFileSize(finalPath);
    }

    public static bool TryDrainPendingForShutdown(TimeSpan timeout)
    {
        Task[] pending;
        lock (s_pendingLock)
        {
            if (s_pendingTasks.Count == 0)
                return true;
            pending = new Task[s_pendingTasks.Count];
            s_pendingTasks.CopyTo(pending);
        }

        try
        {
            return Task.WaitAll(pending, timeout);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.video_mux.diagnostic_observed"
                ),
                ex,
                () =>
                    [
                        ("recording_id", null, BppLogCorrelationPolicy.Short),
                        ("stage", ReplayVideoLogStage.MuxDrain),
                        ("reason_code", ReplayVideoDiagnosticReasonCode.DrainFailed),
                        ("path", null),
                        ("pending_count", PendingTaskCount),
                    ]
            );
            return false;
        }
    }

    public static int PendingTaskCount
    {
        get
        {
            lock (s_pendingLock)
            {
                return s_pendingTasks.Count;
            }
        }
    }

    internal static Task DispatchTracked(Action work)
    {
        if (work == null)
            throw new ArgumentNullException(nameof(work));

        var task = Task.Run(work);
        Track(task);
        return task;
    }

    internal static Task<T> DispatchTracked<T>(Func<T> work)
    {
        if (work == null)
            throw new ArgumentNullException(nameof(work));

        var task = Task.Run(work);
        Track(task);
        return task;
    }

    private static void Track(Task task)
    {
        lock (s_pendingLock)
        {
            s_pendingTasks.Add(task);
        }

        task.ContinueWith(
            static t =>
            {
                lock (s_pendingLock)
                {
                    s_pendingTasks.Remove(t);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
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
                        ("recording_id", null, BppLogCorrelationPolicy.Short),
                        ("stage", ReplayVideoLogStage.TempDelete),
                        ("path", path),
                    ]
            );
        }
    }

    private static void TryDelete(IReadOnlyList<string>? paths)
    {
        if (paths == null)
            return;

        foreach (var path in paths)
            TryDelete(path);
    }
}
