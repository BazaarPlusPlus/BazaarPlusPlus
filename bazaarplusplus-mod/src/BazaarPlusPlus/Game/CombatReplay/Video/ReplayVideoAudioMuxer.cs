#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Second-pass muxer: combines the silent first-pass video with the captured audio WAV into the
/// final MP4. macOS uses AVFoundation and Windows uses Media Foundation. It runs entirely off the
/// main thread. Any mux failure falls
/// back to promoting the silent video so the first-pass product is never lost.
/// </summary>
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

    /// <summary>
    /// Resolves the mux inline on an existing background task. This method never dispatches nested
    /// work, so an encoder drain and its mux remain one shutdown-tracked task.
    /// </summary>
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

    /// <summary>
    /// Runs the platform-native mux pass. H.264 samples are copied and only audio is encoded,
    /// preserving the silent-video fallback contract.
    /// </summary>
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
                    CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                    () =>
                        [
                            CombatReplayVideoLogEvents.MuxRecordingId.Bind(recordingId),
                            CombatReplayVideoLogEvents.MuxStage.Bind(
                                ReplayVideoLogStage.MuxCallback
                            ),
                            CombatReplayVideoLogEvents.MuxReasonCode.Bind(MuxReasonCode.Muxed),
                            CombatReplayVideoLogEvents.MuxPath.Bind(finalPath),
                            CombatReplayVideoLogEvents.MuxPendingCount.Bind(PendingTaskCount),
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
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(recordingId),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.TempDelete
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(tempVideoPath),
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

    /// <summary>
    /// Heuristic guard against a zero-duration mux output. A valid native mux output always carries
    /// the full first-pass video payload and is
    /// least as large as the silent input; a real but tiny recording is still bounded below by that
    /// silent size. So the output is considered zero-duration when it is empty, or when the silent
    /// input size is known and the output is less than half of it (generous slack for container /
    /// faststart differences, yet orders of magnitude above an empty stub). When the silent size is
    /// unknown (0), only a truly empty output is rejected.
    /// </summary>
    internal static bool IsLikelyZeroDurationOutput(long muxedSize, long silentSize)
    {
        if (muxedSize <= 0)
            return true;

        if (silentSize <= 0)
            return false;

        return muxedSize < silentSize / 2;
    }

    /// <summary>
    /// Promotes the silent first-pass video to the final path. Lifts the exact File.Move sequence
    /// previously inlined in <c>CombatReplayVideoRecorder.FinalizeOutputFile</c> so the logic lives
    /// once. Throws on hard IO failure (callers catch). If the temp is already gone, reports the
    /// current size of the final path (idempotent re-finalize).
    /// </summary>
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

    /// <summary>
    /// Best-effort waits for all outstanding tracked finalize/mux tasks to complete, up to
    /// <paramref name="timeout"/>. Intended for app shutdown so in-flight recordings get a chance to
    /// finish; any tasks still running continue in the background and their operation is closed by
    /// the recorder's shutdown sweep. Returns true if all pending tasks completed within the timeout.
    /// </summary>
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
                CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.MuxRecordingId.Bind(null),
                        CombatReplayVideoLogEvents.MuxStage.Bind(ReplayVideoLogStage.MuxDrain),
                        CombatReplayVideoLogEvents.MuxReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.DrainFailed
                        ),
                        CombatReplayVideoLogEvents.MuxPath.Bind(null),
                        CombatReplayVideoLogEvents.MuxPendingCount.Bind(PendingTaskCount),
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

    private static void Track(Task task)
    {
        lock (s_pendingLock)
        {
            s_pendingTasks.Add(task);
        }

        // Untrack on completion regardless of outcome. ContinueWith runs on a pool thread.
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
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(null),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.TempDelete
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(path),
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
