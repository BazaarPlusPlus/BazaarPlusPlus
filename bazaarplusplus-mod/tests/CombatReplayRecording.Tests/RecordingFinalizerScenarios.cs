#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Audio;
using BazaarPlusPlus.Game.CombatReplay.Video;

// Terminal outcomes the recorder's background finalizers report. Each scenario drives the real
// finalizer with a real encoder drain over a scripted encoder and observes the one terminal the
// recording lifecycle publishes.
internal static class RecordingFinalizerScenarios
{
    internal static void Run()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "bpp-recording-finalizer-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            AbortedDrainFailureKeepsAudioAndMetadataOutcomes(root);
            AbortedWithoutDrainReportsAudioAndMetadataOutcomes(root);
            BeginExceptionLeavesActiveOperationToAbortFinalizer(root);
            BeginExceptionClosesOperationTheAbortNeverTook();
            EndedRecordingReportsRealAudioAndMetadataOutcomes(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AbortedDrainFailureKeepsAudioAndMetadataOutcomes(string root)
    {
        var harness = new Harness("aborted-drain-throws");
        var encoder = new ScriptedEncoder { DisposeException = new IOException("dispose") };
        var context = harness.Aborted(
            root,
            encoder,
            capturedFrames: 30,
            Stop(Usable(root, "aborted-drain-throws.wav")),
            Task.FromResult(MetadataWriteOutcome.Complete),
            "superseded",
            ReplayVideoRecordingReasonCode.Superseded
        );

        ReplayVideoRecordingFinalizer.CompleteAbortedRecording(context);

        var terminal = harness.Single();
        Assert(
            terminal.ReasonCode == ReplayVideoRecordingReasonCode.CaptureFailed,
            "A throwing drain still fails the capture."
        );
        Assert(
            terminal.AudioStatus == ReplayVideoAudioStatus.Full,
            "A throwing drain must not erase the tap stop's own outcome."
        );
        Assert(
            terminal.MetadataStatus == ReplayVideoMetadataStatus.Complete,
            "A throwing drain must not erase the start metadata's own outcome."
        );
    }

    private static void AbortedWithoutDrainReportsAudioAndMetadataOutcomes(string root)
    {
        var harness = new Harness("aborted-no-drain");
        var tempPath = Path.Combine(root, "aborted-no-drain.tmp.mp4");
        File.WriteAllBytes(tempPath, new byte[] { 1 });

        ReplayVideoRecordingFinalizer.CompleteAbortedWithoutDrain(
            harness.Operations,
            harness.Operation,
            tempPath,
            Path.Combine(root, "aborted-no-drain.mp4"),
            wavPaths: null,
            Task.FromResult(ReplayAudioStopOutcome.Empty),
            Task.FromResult(
                new MetadataWriteOutcome(
                    ReplayVideoMetadataStatus.Failed,
                    new InvalidOperationException("insert")
                )
            ),
            ReplayVideoRecordingReasonCode.Aborted,
            terminalException: null
        );

        var terminal = harness.Single();
        Assert(
            terminal.ReasonCode == ReplayVideoRecordingReasonCode.Aborted,
            "An abort without a drain keeps its abort reason."
        );
        Assert(
            terminal.AudioStatus == ReplayVideoAudioStatus.Silent,
            "A tap stop without usable audio is silent, not failed."
        );
        Assert(
            terminal.MetadataStatus == ReplayVideoMetadataStatus.Failed,
            "The failed start insert is the reported metadata outcome."
        );
        Assert(!File.Exists(tempPath), "An abort without a drain deletes its temp video.");
    }

    // The start threw after its operation became active, so the abort dispatched a finalizer
    // that owns the terminal. The start's catch runs while that finalizer is still draining.
    private static void BeginExceptionLeavesActiveOperationToAbortFinalizer(string root)
    {
        var harness = new Harness("begin-exception-race");
        using var drainRelease = new ManualResetEventSlim(false);
        var encoder = new ScriptedEncoder { CompletionGate = drainRelease };
        var context = harness.Aborted(
            root,
            encoder,
            capturedFrames: 30,
            Stop(Usable(root, "begin-exception-race.wav")),
            Task.FromResult(MetadataWriteOutcome.Complete),
            "begin-exception",
            ReplayVideoRecordingReasonCode.BeginException
        );
        var finalizer = Task.Run(() =>
            ReplayVideoRecordingFinalizer.CompleteAbortedRecording(context)
        );
        Assert(encoder.Draining.Wait(TimeSpan.FromSeconds(5)), "The abort finalizer must drain.");

        ReplayVideoRecordingFinalizer.CompleteBeginException(
            harness.Operations,
            harness.Operation,
            abortedOperation: harness.Operation,
            new InvalidOperationException("begin")
        );
        Assert(
            harness.Terminals.Count == 0,
            "The start's catch must not close an operation the abort finalizer owns."
        );

        drainRelease.Set();
        Assert(finalizer.Wait(TimeSpan.FromSeconds(5)), "The abort finalizer must finish.");
        var terminal = harness.Single();
        Assert(
            terminal.ReasonCode == ReplayVideoRecordingReasonCode.BeginException,
            "The finalizer keeps the begin-exception reason."
        );
        Assert(
            terminal.AudioStatus == ReplayVideoAudioStatus.Full
                && terminal.MetadataStatus == ReplayVideoMetadataStatus.Complete,
            "The finalizer reports the real audio and metadata outcomes."
        );
        Assert(
            terminal.ArtifactUsable,
            "A begin exception after a usable capture keeps the promoted video."
        );
    }

    private static void BeginExceptionClosesOperationTheAbortNeverTook()
    {
        var harness = new Harness("begin-exception-preactive");

        ReplayVideoRecordingFinalizer.CompleteBeginException(
            harness.Operations,
            harness.Operation,
            abortedOperation: null,
            new InvalidOperationException("begin")
        );

        var terminal = harness.Single();
        Assert(
            terminal.ReasonCode == ReplayVideoRecordingReasonCode.BeginException
                && terminal.AudioStatus == ReplayVideoAudioStatus.Failed
                && terminal.MetadataStatus == ReplayVideoMetadataStatus.Unavailable,
            "A start that failed before activation closes with the begin-exception fallback."
        );
    }

    private static void EndedRecordingReportsRealAudioAndMetadataOutcomes(string root)
    {
        var silent = new Harness("ended-metadata-failed");
        silent.Ended(
            root,
            ReplayAudioStopOutcome.Empty,
            new MetadataWriteOutcome(
                ReplayVideoMetadataStatus.Failed,
                new InvalidOperationException("insert")
            )
        );
        var silentTerminal = silent.Single();
        Assert(silentTerminal.ArtifactUsable, "A silent ended recording keeps its video.");
        Assert(
            silentTerminal.AudioStatus == ReplayVideoAudioStatus.Silent
                && silentTerminal.MetadataStatus == ReplayVideoMetadataStatus.Failed,
            "An ended recording reports silent audio and its failed start insert."
        );
        Assert(
            silentTerminal.ReasonCode == ReplayVideoRecordingReasonCode.MetadataFailed,
            "The start insert failure takes precedence over missing audio."
        );

        var stopFailed = new Harness("ended-audio-stop-failed");
        stopFailed.Ended(
            root,
            Stop(
                new ReplayAudioCaptureResult
                {
                    WavPath = Path.Combine(root, "ended-audio-stop-failed.wav"),
                    FailureReason = ReplayAudioFailureReasonCode.BackendStopFailed,
                    FailureException = new InvalidOperationException("stop"),
                }
            ),
            MetadataWriteOutcome.Complete
        );
        var stopTerminal = stopFailed.Single();
        Assert(stopTerminal.ArtifactUsable, "A failed tap stop keeps the silent video.");
        Assert(
            stopTerminal.AudioStatus == ReplayVideoAudioStatus.Failed
                && stopTerminal.MetadataStatus == ReplayVideoMetadataStatus.Complete,
            "An ended recording reports the failed tap stop and its complete start insert."
        );
        Assert(
            stopTerminal.ReasonCode == ReplayVideoRecordingReasonCode.AudioStopFailed,
            "A failed tap stop is the degradation reason."
        );
    }

    private static ReplayAudioStopOutcome Stop(params ReplayAudioCaptureResult[] results) =>
        new(results);

    private static ReplayAudioCaptureResult Usable(string root, string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[] { 1 });
        return new ReplayAudioCaptureResult { WavPath = path, Usable = true };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Harness
    {
        private readonly string _name;

        internal Harness(string name)
        {
            _name = name;
            Operations = new ReplayVideoRecordingLifecycle(() => name, Terminals.Add);
            Operation = Operations.Start(
                "battle-" + name,
                CombatReplayPlaybackSource.ImportedGhost,
                DateTimeOffset.UtcNow
            );
        }

        internal ReplayVideoRecordingLifecycle Operations { get; }
        internal ReplayVideoRecordingOperation Operation { get; }
        internal List<ReplayVideoRecordingTerminal> Terminals { get; } = new();

        internal ReplayVideoRecordingTerminal Single()
        {
            Assert(Terminals.Count == 1, $"{_name} must close exactly once.");
            return Terminals[0];
        }

        internal AbortedRecordingFinalizeContext Aborted(
            string root,
            ScriptedEncoder encoder,
            int capturedFrames,
            ReplayAudioStopOutcome audio,
            Task<MetadataWriteOutcome> startMetadata,
            string endReason,
            ReplayVideoRecordingReasonCode terminalReason
        ) =>
            new(
                Recording(root, encoder, capturedFrames, audio),
                endReason,
                Store: null,
                VideoDir: root,
                startMetadata,
                terminalReason,
                TerminalException: null
            );

        internal void Ended(string root, ReplayAudioStopOutcome audio, MetadataWriteOutcome start)
        {
            ReplayVideoRecordingFinalizer.CompleteEndedRecording(
                new EndedRecordingFinalizeContext(
                    Recording(root, new ScriptedEncoder(), capturedFrames: 30, audio),
                    new ReplayVideoAudioMuxer(),
                    Store: null,
                    VideoDir: root,
                    new RecordingStartInputs(Task.FromResult(start), null, null)
                )
            );
        }

        private RecordingFinalizeContext Recording(
            string root,
            ScriptedEncoder encoder,
            int capturedFrames,
            ReplayAudioStopOutcome audio
        )
        {
            var tempPath = Path.Combine(root, _name + ".tmp.mp4");
            var finalPath = Path.Combine(root, _name + ".mp4");
            File.WriteAllBytes(tempPath, new byte[] { 1, 2, 3 });
            var request = new ReplayVideoCaptureRequest
            {
                VideoId = _name,
                BattleId = "battle-" + _name,
                Source = CombatReplayPlaybackSource.ImportedGhost,
                OutputFilePath = tempPath,
                FinalOutputFilePath = finalPath,
                OutputDirectoryPath = root,
                Width = 64,
                Height = 64,
                Fps = 30,
                EncoderProfile = ReplayVideoEncoderProfile.NativeVideoToolbox(64, 64, 30),
                BufferPlan = ReplayVideoBufferPlan.Create(64, 64),
            };
            var drain = new ReplayVideoEncoderDrain(
                new ReplayVideoEncoderDrainInput(
                    encoder,
                    request,
                    DateTimeOffset.UtcNow,
                    capturedFrames,
                    DroppedFrames: 0,
                    RepeatedFrames: 0,
                    FailureReasonCode: null,
                    DegradationReasonCode: null,
                    FailureException: null,
                    FrameByteLength: 64 * 64 * 4,
                    NativeSlotCount: 0,
                    CfrCopyP95Us: 0
                )
            );
            return new RecordingFinalizeContext(
                Operations,
                Operation,
                drain,
                tempPath,
                finalPath,
                audio.UsableWavPaths,
                Task.FromResult(audio)
            );
        }
    }

    private sealed class ScriptedEncoder : IReplayVideoEncoder
    {
        internal ManualResetEventSlim Draining { get; } = new(false);
        internal ManualResetEventSlim? CompletionGate { get; init; }
        internal Exception? DisposeException { get; init; }

        public bool WriterFailed => false;

        public void SignalEndOfStream() { }

        public ReplayVideoEncoderCompletionOutcome WaitForCompletion(TimeSpan timeout)
        {
            Draining.Set();
            CompletionGate?.Wait(timeout);
            return ReplayVideoEncoderCompletionOutcome.Success(string.Empty);
        }

        public void Dispose()
        {
            if (DisposeException != null)
                throw DisposeException;
        }
    }
}
