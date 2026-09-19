using System.Reflection;
using BepInEx.Logging;

// Behavioral unit tests for the design-mandated pure-logic units of the
// combat-replay audio/video pipeline, reached as internal types via reflection
// (Type.GetType("Full.Name, BazaarPlusPlus")):
//   1) WavStreamWriter    -- byte-exact WAV header
//   2) WallClockCfrPacer  -- wall-clock repeat/drop counting
// Plus native encoder policy, timing, mux fallback and lifecycle contracts.

WavHeaderTests.Run();
CfrPacerTests.Run();
NativeFrameSubmissionPlanTests.Run();
VideoEncoderProfileTests.Run();
VideoBackendPolicyTests.Run();
VideoBufferPlanTests.Run();
CopyTimingTests.Run();
EncoderDrainTests.Run();
OutputFileNameTests.Run();
ZeroDurationMuxGuardTests.Run();
AudioTapPlanTests.Run();
RecordingOperationContractTests.Run();
MediaEventCatalogTests.Run();
AudioStopTimeoutTests.Run();
RecorderIntegrationContractTests.Run();

Console.WriteLine("CombatReplayAudioVideo tests passed.");

// ---------------------------------------------------------------------------
// 1) WavStreamWriter.BuildHeader: byte-exact 44-byte IEEE-float WAV header.
// ---------------------------------------------------------------------------
file static class WavHeaderTests
{
    private static readonly Type WavType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Audio.WavStreamWriter"
    );

    public static void Run()
    {
        StereoHeader();
        MonoEmptyHeader();
    }

    private static void StereoHeader()
    {
        // 48000 Hz, 2ch, dataByteLength = 8.
        var header = BuildHeader(48000, 2, 8);
        TestReflection.Assert(header.Length == 44, "WAV header must be exactly 44 bytes.");

        TestReflection.Assert(Ascii(header, 0, 4) == "RIFF", "bytes 0-3 must be 'RIFF'.");
        TestReflection.Assert(
            U32(header, 4) == 36u + 8u,
            "bytes 4-7 must be LE (36+dataByteLength)=44."
        );
        TestReflection.Assert(Ascii(header, 8, 4) == "WAVE", "bytes 8-11 must be 'WAVE'.");
        TestReflection.Assert(Ascii(header, 12, 4) == "fmt ", "bytes 12-15 must be 'fmt '.");
        TestReflection.Assert(
            U32(header, 16) == 16u,
            "bytes 16-19 must be LE 16 (fmt chunk size)."
        );
        TestReflection.Assert(
            U16(header, 20) == 3,
            "bytes 20-21 must be LE 3 (WAVE_FORMAT_IEEE_FLOAT)."
        );
        TestReflection.Assert(U16(header, 22) == 2, "bytes 22-23 must be LE 2 (channels).");
        TestReflection.Assert(
            U32(header, 24) == 48000u,
            "bytes 24-27 must be LE 48000 (sample rate)."
        );
        TestReflection.Assert(
            U32(header, 28) == 48000u * 2u * 4u,
            "bytes 28-31 must be LE byteRate=384000."
        );
        TestReflection.Assert(U16(header, 32) == 2 * 4, "bytes 32-33 must be LE blockAlign=8.");
        TestReflection.Assert(U16(header, 34) == 32, "bytes 34-35 must be LE 32 (bitsPerSample).");
        TestReflection.Assert(Ascii(header, 36, 4) == "data", "bytes 36-39 must be 'data'.");
        TestReflection.Assert(U32(header, 40) == 8u, "bytes 40-43 must be LE 8 (dataByteLength).");
    }

    private static void MonoEmptyHeader()
    {
        // 44100 Hz, 1ch, dataByteLength = 0 (placeholder header on file open).
        var header = BuildHeader(44100, 1, 0);
        TestReflection.Assert(header.Length == 44, "Mono header must be exactly 44 bytes.");
        TestReflection.Assert(U16(header, 22) == 1, "Mono header channels must be 1.");
        TestReflection.Assert(U32(header, 24) == 44100u, "Mono header sample rate must be 44100.");
        TestReflection.Assert(
            U32(header, 28) == 44100u * 1u * 4u,
            "Mono byteRate must be 44100*1*4=176400."
        );
        TestReflection.Assert(U16(header, 32) == 1 * 4, "Mono blockAlign must be 4.");
        TestReflection.Assert(U16(header, 34) == 32, "Mono bitsPerSample must be 32.");
        TestReflection.Assert(
            U32(header, 4) == 36u,
            "Empty-data riffChunkSize must be 36 (=36+0)."
        );
        TestReflection.Assert(U32(header, 40) == 0u, "Empty-data dataChunkSize must be 0.");
    }

    private static byte[] BuildHeader(int sampleRate, int channels, int dataByteLength)
    {
        var method =
            WavType.GetMethod(
                "BuildHeader",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            ) ?? throw new InvalidOperationException("WavStreamWriter.BuildHeader not found.");
        return (byte[])(
            method.Invoke(null, new object[] { sampleRate, channels, dataByteLength })
            ?? throw new InvalidOperationException("BuildHeader returned null.")
        );
    }

    private static string Ascii(byte[] b, int offset, int len)
    {
        var chars = new char[len];
        for (var i = 0; i < len; i++)
            chars[i] = (char)b[offset + i];
        return new string(chars);
    }

    // Decode little-endian explicitly (do not rely on host endianness).
    private static uint U32(byte[] b, int o) =>
        (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
}

// ---------------------------------------------------------------------------
// 2) WallClockCfrPacer: wall-clock CFR repeat/drop counting via CfrTickResult.
// ---------------------------------------------------------------------------
file static class CfrPacerTests
{
    private static readonly Type PacerType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.WallClockCfrPacer"
    );

    public static void Run()
    {
        NoLeadingPad();
        SteadyCadenceNoRepeats();
        RepeatsWhenCaptureStalls();
        DropOnOvershootResync();
    }

    // First tick with no frame available: nothing emitted, clock not started.
    private static void NoLeadingPad()
    {
        var pacer = New(30);
        long lastEmitted = -1;
        var r = Tick(pacer, now: 0.0, hasFrame: false, latestSeq: 0, ref lastEmitted);
        TestReflection.Assert(
            r.Emit == 0 && r.Repeat == 0 && r.Dropped == 0,
            "No leading pad: empty tick before first frame."
        );

        // Still not started: even a much later no-frame tick stays empty.
        r = Tick(pacer, now: 5.0, hasFrame: false, latestSeq: 0, ref lastEmitted);
        TestReflection.Assert(
            r.Emit == 0 && r.Repeat == 0 && r.Dropped == 0,
            "No-frame ticks must not start the clock or emit."
        );
    }

    // 30 fps, clock advances exactly one frame interval per tick, a NEW seq each
    // tick: total emits == 30, repeats == 0 over the simulated second.
    private static void SteadyCadenceNoRepeats()
    {
        var pacer = New(30);
        var interval = 1.0 / 30.0;
        long lastEmitted = -1;
        long seq = 0;
        var now = 0.0;
        var totalEmit = 0;
        var totalRepeat = 0;
        var totalDropped = 0;

        for (var i = 0; i < 30; i++)
        {
            var r = Tick(pacer, now, hasFrame: true, latestSeq: seq, ref lastEmitted);
            totalEmit += r.Emit;
            totalRepeat += r.Repeat;
            totalDropped += r.Dropped;
            now += interval;
            seq++; // brand-new source frame each render tick
        }

        TestReflection.Assert(
            totalEmit == 30,
            $"Steady 30fps cadence should emit exactly 30 slots, got {totalEmit}."
        );
        TestReflection.Assert(
            totalRepeat == 0,
            $"One new frame per slot means zero repeats, got {totalRepeat}."
        );
        TestReflection.Assert(
            totalDropped == 0,
            $"Steady cadence should not drop, got {totalDropped}."
        );
    }

    // After priming one frame, advance the clock by 3 intervals while keeping
    // latestSeq CONSTANT: the extra slots repeat the last frame.
    private static void RepeatsWhenCaptureStalls()
    {
        var pacer = New(30);
        var interval = 1.0 / 30.0;
        long lastEmitted = -1;
        const long frozenSeq = 7;

        // Prime: first frame at t=0 establishes the timeline and emits slot 0 (new).
        var prime = Tick(pacer, now: 0.0, hasFrame: true, latestSeq: frozenSeq, ref lastEmitted);
        TestReflection.Assert(
            prime.Emit == 1 && prime.Repeat == 0,
            "Priming tick should emit exactly the first (new) frame."
        );

        // Advance ~3 intervals total with the SAME seq across a few ticks, staying
        // under the overshoot resync threshold so nothing is dropped.
        var totalEmit = prime.Emit;
        var totalRepeat = prime.Repeat;
        var totalDropped = prime.Dropped;
        var now = 0.0;
        for (var i = 0; i < 3; i++)
        {
            now += interval;
            var r = Tick(pacer, now, hasFrame: true, latestSeq: frozenSeq, ref lastEmitted);
            totalEmit += r.Emit;
            totalRepeat += r.Repeat;
            totalDropped += r.Dropped;
        }

        // Slots elapsed = 4 (slot0 prime + 3 more). Emit==slots; the 3 beyond the
        // first duplicate the frozen frame => RepeatCount == slots-1.
        TestReflection.Assert(
            totalEmit == 4,
            $"Four CFR slots should be emitted across the stall, got {totalEmit}."
        );
        TestReflection.Assert(
            totalRepeat == 3,
            $"Slots beyond the first must repeat the last frame: expected 3 repeats, got {totalRepeat}."
        );
        TestReflection.Assert(
            totalDropped == 0,
            $"Stall under the resync threshold should drop nothing, got {totalDropped}."
        );
    }

    // Prime, then jump the clock far past resyncOvershootIntervals*interval in a
    // single tick. EmitCount is clamped by maxEmitsPerTick=3, DroppedCount>0, and
    // the pacer resyncs so the next steady tick emits normally (0 or 1).
    private static void DropOnOvershootResync()
    {
        // maxEmitsPerTick=3, resyncOvershootIntervals=5.
        var pacer = New(30, maxEmitsPerTick: 3, resyncOvershootIntervals: 5);
        var interval = 1.0 / 30.0;
        long lastEmitted = -1;
        long seq = 0;

        // Prime at t=0.
        var prime = Tick(pacer, now: 0.0, hasFrame: true, latestSeq: seq, ref lastEmitted);
        TestReflection.Assert(prime.Emit == 1, "Priming tick should emit the first frame.");
        seq++;

        // Single huge jump: ~10 intervals past the last emit time in one tick.
        var jumpNow = 10.0 * interval;
        var r = Tick(pacer, jumpNow, hasFrame: true, latestSeq: seq, ref lastEmitted);
        TestReflection.Assert(r.Emit <= 3, $"maxEmitsPerTick clamp must hold (emit={r.Emit}).");
        TestReflection.Assert(
            r.Dropped > 0,
            $"Overshoot resync must count skipped slots as dropped (dropped={r.Dropped})."
        );

        // After resync the clock is realigned to jumpNow+interval, so a tick a hair
        // before that emits 0, and a tick one interval later emits exactly 1.
        var before = Tick(
            pacer,
            jumpNow + (interval * 0.5),
            hasFrame: true,
            latestSeq: ++seq,
            ref lastEmitted
        );
        TestReflection.Assert(
            before.Emit == 0 && before.Dropped == 0,
            $"Resync should not emit early (emit={before.Emit})."
        );

        var after = Tick(
            pacer,
            jumpNow + (interval * 1.5),
            hasFrame: true,
            latestSeq: ++seq,
            ref lastEmitted
        );
        TestReflection.Assert(
            after.Emit == 1 && after.Dropped == 0,
            $"After resync, steady cadence should resume emitting one slot (emit={after.Emit}, dropped={after.Dropped})."
        );
    }

    private static object New(int fps) =>
        Activator.CreateInstance(PacerType, fps, 3, 5)
        ?? throw new InvalidOperationException("WallClockCfrPacer should be constructible.");

    private static object New(int fps, int maxEmitsPerTick, int resyncOvershootIntervals) =>
        Activator.CreateInstance(PacerType, fps, maxEmitsPerTick, resyncOvershootIntervals)
        ?? throw new InvalidOperationException("WallClockCfrPacer should be constructible.");

    private static TickResult Tick(
        object pacer,
        double now,
        bool hasFrame,
        long latestSeq,
        ref long lastEmittedSeq
    )
    {
        var method =
            PacerType.GetMethod(
                "Tick",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException("WallClockCfrPacer.Tick not found.");

        var args = new object[] { now, hasFrame, latestSeq, lastEmittedSeq };
        var result =
            method.Invoke(pacer, args)
            ?? throw new InvalidOperationException("Tick returned null.");
        lastEmittedSeq = (long)args[3]; // ref parameter is written back into the args array

        var rt = result.GetType();
        return new TickResult(
            (int)TestReflection.GetField(rt, result, "EmitCount")!,
            (int)TestReflection.GetField(rt, result, "RepeatCount")!,
            (int)TestReflection.GetField(rt, result, "DroppedCount")!
        );
    }

    private readonly record struct TickResult(int Emit, int Repeat, int Dropped);
}

// ---------------------------------------------------------------------------
// 3) NativeFrameSubmissionPlan: never repay missed wall-clock slots with one
//    newer Metal texture.
// ---------------------------------------------------------------------------
file static class NativeFrameSubmissionPlanTests
{
    private static readonly Type PlanType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.NativeFrameSubmissionPlan"
    );

    public static void Run()
    {
        PacerResyncDoesNotCreatePtsGap();
        ConsecutiveMissesDoNotAccumulateDebt();
        AccountingCloses();
    }

    private static void PacerResyncDoesNotCreatePtsGap()
    {
        var plan = Create(emit: 3, repeat: 2, dropped: 120);
        TestReflection.Assert(
            Read(plan, "EncodeFrameCount") == 3,
            "A large pacer resync must not expand the native encode batch."
        );
        TestReflection.Assert(
            Read(plan, "TimelineFrameCount") == 3,
            "Pacer resync slots must not create a visible gap in the output timeline."
        );
    }

    private static void ConsecutiveMissesDoNotAccumulateDebt()
    {
        var first = Create(emit: 1, repeat: 0, dropped: 0);
        var second = Create(emit: 1, repeat: 0, dropped: 0);
        TestReflection.Assert(
            Read(first, "EncodeFrameCount") == 1 && Read(second, "EncodeFrameCount") == 1,
            "Independent ticks must never carry a deferred encode batch forward."
        );
    }

    private static void AccountingCloses()
    {
        var plan = Create(emit: 3, repeat: 2, dropped: 7);
        var accounted = Read(plan, "CapturedFrameCount") + Read(plan, "RepeatedFrameCount");
        TestReflection.Assert(
            accounted == Read(plan, "TimelineFrameCount"),
            "Captured and repeated frames must exactly cover the packed output timeline."
        );
        TestReflection.Assert(
            Read(plan, "DroppedFrameCountOnSubmissionFailure") == 10,
            "A failed native submission must count both its encode candidates and pacer drops."
        );
    }

    private static object Create(int emit, int repeat, int dropped)
    {
        var method =
            PlanType.GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("NativeFrameSubmissionPlan.Create not found.");
        return method.Invoke(null, new object[] { emit, repeat, dropped })
            ?? throw new InvalidOperationException(
                "NativeFrameSubmissionPlan.Create returned null."
            );
    }

    private static int Read(object plan, string propertyName) =>
        (int)(TestReflection.GetProp(PlanType, plan, propertyName) ?? -1);
}

// ---------------------------------------------------------------------------
// 4) ReplayVideoFramePool: optional Rent/Return + cap micro-check.
// ---------------------------------------------------------------------------
file static class VideoEncoderProfileTests
{
    private static readonly Type ProfileType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoEncoderProfile"
    );

    public static void Run()
    {
        var bitrate = ProfileType.GetMethod(
            "CalculateTargetBitrateKbps",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        )!;
        int Target(int width, int height, int fps) =>
            (int)bitrate.Invoke(null, new object[] { width, height, fps })!;
        TestReflection.Assert(
            Target(640, 360, 15) == 6000,
            "Native bitrate must enforce 6 Mbps minimum."
        );
        TestReflection.Assert(
            Target(2742, 1624, 60) == 24000,
            "Native bitrate must enforce 24 Mbps maximum."
        );

        var mac = ProfileType
            .GetMethod(
                "NativeVideoToolbox",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(null, new object[] { 1920, 1080, 60 })!;
        var windows = ProfileType
            .GetMethod(
                "NativeMediaFoundation",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(null, new object[] { 1920, 1080, 60 })!;
        TestReflection.Assert(
            (string)TestReflection.GetProp(ProfileType, mac, "Codec")! == "h264_videotoolbox",
            "macOS must select VideoToolbox."
        );
        TestReflection.Assert(
            (string)TestReflection.GetProp(ProfileType, windows, "Codec")!
                == "h264_media_foundation",
            "Windows must select Media Foundation."
        );
        TestReflection.Assert(
            (string)TestReflection.GetProp(ProfileType, mac, "PixelFormat")! == "nv12",
            "Native video input must be NV12."
        );
        TestReflection.Assert(
            (string)TestReflection.GetProp(ProfileType, windows, "RateControlSummary")!
                == "avg_bitrate=18662k",
            "Telemetry must describe the average-bitrate control actually configured natively."
        );
    }
}

// ---------------------------------------------------------------------------
// 4b) Platform backend contract: supported desktop platforms are native and
//     unknown platforms are rejected.
// ---------------------------------------------------------------------------
file static class VideoBackendPolicyTests
{
    public static void Run()
    {
        var policyType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoBackendPolicy"
        );
        var platformType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.VideoEncoderPlatform"
        );
        var backendType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoBackend"
        );
        var forPlatform =
            policyType.GetMethod(
                "ForPlatform",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Replay backend platform selector not found.");
        object Backend(string platform) =>
            forPlatform.Invoke(null, new[] { Enum.Parse(platformType, platform) })!;

        var mac = Backend("MacOS");
        TestReflection.Assert(
            mac.Equals(Enum.Parse(backendType, "MacNative")),
            "macOS must use the native replay backend."
        );

        var windows = Backend("Windows");
        TestReflection.Assert(
            windows.Equals(Enum.Parse(backendType, "WindowsNative")),
            "Windows must use the native Media Foundation backend."
        );
        var other = Backend("Other");
        TestReflection.Assert(
            other.Equals(Enum.Parse(backendType, "Unsupported")),
            "Unknown platforms must be rejected."
        );

        TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.MacNativeReplayAudioMuxer"
        );
        TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.WindowsNativeReplayAudioMuxer"
        );
    }
}

// ---------------------------------------------------------------------------
// 5) Byte budget: pool payload is bounded independently from queue references,
//    and the writer gets one headroom slot (queue = pool - 1).
// ---------------------------------------------------------------------------
file static class VideoBufferPlanTests
{
    private static readonly Type PlanType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoBufferPlan"
    );

    public static void Run()
    {
        InvalidDimensionsReportPreciseParameter();

        var native = Create(2742, 1624);
        TestReflection.Assert(
            Int(native, "FrameByteLength") == 17_812_032,
            "Native frame bytes drifted."
        );
        TestReflection.Assert(Int(native, "PoolCapacity") == 7, "Native pool capacity must be 7.");
        TestReflection.Assert(
            Int(native, "QueueCapacity") == 6,
            "Queue must reserve one writer slot."
        );
        TestReflection.Assert(
            Long(native, "PoolPayloadBytes") == 124_684_224,
            "Native pool payload must be ~119 MiB."
        );
        TestReflection.Assert(
            !Bool(native, "BudgetExceeded"),
            "Native pool payload must remain within 128 MiB."
        );

        var fourK = Create(3840, 2160);
        TestReflection.Assert(Int(fourK, "PoolCapacity") == 4, "4K pool capacity must be 4.");
        TestReflection.Assert(Int(fourK, "QueueCapacity") == 3, "4K queue capacity must be 3.");

        var eightK = Create(7680, 4320);
        TestReflection.Assert(
            Int(eightK, "PoolCapacity") == 3,
            "The forward-progress floor must remain 3."
        );
        TestReflection.Assert(
            Bool(eightK, "BudgetExceeded"),
            "An unavoidable minimum-capacity overrun must be explicit."
        );
    }

    private static void InvalidDimensionsReportPreciseParameter()
    {
        AssertInvalidDimension(-1, 1080, "width");
        AssertInvalidDimension(1920, 0, "height");
    }

    private static void AssertInvalidDimension(int width, int height, string expectedParameter)
    {
        try
        {
            Create(width, height);
            throw new InvalidOperationException("Invalid video dimensions must be rejected.");
        }
        catch (TargetInvocationException ex)
            when (ex.InnerException is ArgumentOutOfRangeException argumentException)
        {
            TestReflection.Assert(
                argumentException.ParamName == expectedParameter,
                $"Invalid {expectedParameter} must identify the matching parameter."
            );
        }
    }

    private static object Create(int width, int height) =>
        PlanType
            .GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(null, new object[] { width, height, 128L * 1024 * 1024 })!;

    private static int Int(object value, string name) =>
        (int)TestReflection.GetProp(PlanType, value, name)!;

    private static long Long(object value, string name) =>
        (long)TestReflection.GetProp(PlanType, value, name)!;

    private static bool Bool(object value, string name) =>
        (bool)TestReflection.GetProp(PlanType, value, name)!;
}

// ---------------------------------------------------------------------------
// 6) Readback limiter: reservations never exceed two and a released slot can be
//    reused without blocking the Unity main thread.
// ---------------------------------------------------------------------------
file static class CopyTimingTests
{
    public static void Run()
    {
        var type = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoCopyTimingAccumulator"
        );
        var timing = Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { 5 },
            culture: null
        )!;
        foreach (var value in new long[] { 1, 2, 3, 4, 100 })
        {
            TestReflection.Invoke(
                type,
                timing,
                "ObserveMicroseconds",
                new[] { typeof(long) },
                new object[] { value }
            );
        }
        TestReflection.Assert(
            (int)TestReflection.GetProp(type, timing, "SampleCount")! == 5,
            "Copy timing must retain its bounded sample count."
        );
        TestReflection.Assert(
            (long)TestReflection.GetProp(type, timing, "P95Microseconds")! == 100,
            "Copy timing p95 must select the 95th percentile sample."
        );
    }
}

// ---------------------------------------------------------------------------
// 8) Encoder cleanup: concurrent Dispose calls execute cleanup once.
// ---------------------------------------------------------------------------
file static class EncoderDrainTests
{
    private static readonly Type DrainType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoEncoderDrain"
    );
    private static readonly Type EncoderReasonType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoEncoderFailureReasonCode"
    );
    private static readonly Type RecordingReasonType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingReasonCode"
    );

    public static void Run()
    {
        var mapReason =
            DrainType.GetMethod(
                "MapReason",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { EncoderReasonType },
                modifiers: null
            )
            ?? throw new InvalidOperationException("ReplayVideoEncoderDrain.MapReason not found.");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["None"] = "EncoderWriterFailed",
            ["WriterTimeout"] = "EncoderTimeout",
            ["ProcessTimeout"] = "EncoderTimeout",
            ["NonZeroExit"] = "EncoderNonZeroExit",
            ["StdinUnavailable"] = "EncoderWriterFailed",
            ["StdinWriteFailed"] = "EncoderWriterFailed",
            ["StdinCloseFailed"] = "EncoderWriterFailed",
            ["WriterCrashed"] = "EncoderWriterFailed",
        };

        foreach (var pair in expected)
        {
            var actual = mapReason.Invoke(null, new[] { Enum.Parse(EncoderReasonType, pair.Key) });
            TestReflection.Assert(
                Equals(actual, Enum.Parse(RecordingReasonType, pair.Value)),
                $"Encoder reason {pair.Key} must map to {pair.Value}."
            );
        }
    }

    private static void SetProperty(Type type, object instance, string name, object value)
    {
        var property =
            type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");
        property.SetValue(instance, value);
    }
}

// ---------------------------------------------------------------------------
// 10) Replay output names include the recording identity, so overlapping drains
//     for the same battle and wall-clock second cannot share either artifact.
// ---------------------------------------------------------------------------
file static class OutputFileNameTests
{
    public static void Run()
    {
        var type = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoOutputFileNames"
        );
        var create =
            type.GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string), typeof(string), typeof(string) },
                modifiers: null
            )
            ?? throw new InvalidOperationException("ReplayVideoOutputFileNames.Create not found.");
        var firstId = "0123456789abcdef0123456789abcdef";
        var secondId = "fedcba9876543210fedcba9876543210";
        var first = create.Invoke(null, new object[] { "battle", "20260715-120000", firstId })!;
        var second = create.Invoke(null, new object[] { "battle", "20260715-120000", secondId })!;
        var firstFinal = (string)TestReflection.GetProp(type, first, "FinalFileName")!;
        var firstTemp = (string)TestReflection.GetProp(type, first, "TempFileName")!;
        var secondFinal = (string)TestReflection.GetProp(type, second, "FinalFileName")!;
        var secondTemp = (string)TestReflection.GetProp(type, second, "TempFileName")!;

        TestReflection.Assert(firstFinal != secondFinal, "Final output names must be unique.");
        TestReflection.Assert(firstTemp != secondTemp, "Temp output names must be unique.");
        TestReflection.Assert(
            firstFinal == $"battle.20260715-120000.{firstId}.mp4",
            "The final name must carry the full unique recording identity."
        );
        TestReflection.Assert(
            firstTemp == $"battle.20260715-120000.{firstId}.recording.mp4",
            "The temp name must carry the same unique recording identity."
        );
    }
}

// ---------------------------------------------------------------------------
// 11) ReplayVideoAudioMuxer.IsLikelyZeroDurationOutput: the gate that stops a
//    -shortest mux of an empty WAV (exit 0, ~hundred-byte stub) from deleting the
//    good silent first-pass video. Reproduced sizes: a real 3s recording is
//    multi-MB; the empty-output stub is ~261 bytes.
// ---------------------------------------------------------------------------
file static class ZeroDurationMuxGuardTests
{
    private static readonly Type MuxerType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer"
    );

    public static void Run()
    {
        // The repro: a ~261-byte empty-output stub against a multi-MB silent video
        // must be flagged as zero-duration so the muxer keeps the silent recording.
        TestReflection.Assert(
            Guard(261, 3_000_000),
            "An empty-output stub far smaller than the silent video must be flagged zero-duration."
        );

        // A real muxed file (slightly larger than the silent input, since -c:v copy
        // carries the full video payload plus an AAC track) must NOT be flagged.
        TestReflection.Assert(
            !Guard(3_100_000, 3_000_000),
            "A muxed file at least the size of the silent video must not be flagged."
        );

        // A tiny but legitimate recording: output >= silent size is always accepted.
        TestReflection.Assert(
            !Guard(5_000, 4_000),
            "A small but valid output >= silent size must not be flagged."
        );

        // An empty output (0 bytes) is always zero-duration, regardless of silent size.
        TestReflection.Assert(Guard(0, 3_000_000), "A zero-byte output must be flagged.");
        TestReflection.Assert(
            Guard(0, 0),
            "A zero-byte output must be flagged even if silent size is unknown."
        );

        // When the silent size is unknown (0), only a truly empty output is rejected;
        // a non-empty output cannot be measured against a baseline so it is accepted.
        TestReflection.Assert(
            !Guard(200, 0),
            "With unknown silent size, a non-empty output must not be flagged."
        );

        // Exactly half the silent size is the boundary: strictly-less-than-half is the
        // rejection rule, so == half is accepted and just-under-half is rejected.
        TestReflection.Assert(
            !Guard(500, 1000),
            "Output exactly half the silent size must be accepted (boundary)."
        );
        TestReflection.Assert(
            Guard(499, 1000),
            "Output just under half the silent size must be flagged."
        );
    }

    private static bool Guard(long muxedSize, long silentSize)
    {
        var method =
            MuxerType.GetMethod(
                "IsLikelyZeroDurationOutput",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            )
            ?? throw new InvalidOperationException(
                "ReplayVideoAudioMuxer.IsLikelyZeroDurationOutput not found."
            );
        return (bool)(
            method.Invoke(null, new object[] { muxedSize, silentSize })
            ?? throw new InvalidOperationException("IsLikelyZeroDurationOutput returned null.")
        );
    }
}

// ---------------------------------------------------------------------------
// 5) ReplayVideoAudioMuxer.BuildArguments: multi-WAV capture must mix the base
//    bus audio and SFX bus audio into one AAC input via amix.
// ---------------------------------------------------------------------------
file static class AudioTapPlanTests
{
    private static readonly Type TapPlanType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioTapPlan"
    );

    public static void Run()
    {
        DerivesSingleCoreMasterWavPath();
    }

    private static void DerivesSingleCoreMasterWavPath()
    {
        var paths = DeriveAudioWavPaths(@"C:\replays\battle.20260530-105445.recording.mp4");

        var expected = new[] { @"C:\replays\battle.20260530-105445.audio.wav" };

        TestReflection.Assert(
            paths.Count == expected.Length,
            $"Expected {expected.Length} audio tap paths, got {paths.Count}."
        );
        for (var i = 0; i < expected.Length; i++)
        {
            TestReflection.Assert(
                paths[i] == expected[i],
                $"Audio tap path {i} was {paths[i]}, expected {expected[i]}."
            );
        }
    }

    private static IReadOnlyList<string> DeriveAudioWavPaths(string tempVideoPath)
    {
        var method =
            TapPlanType.GetMethod(
                "DeriveAudioWavPaths",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                "ReplayVideoAudioTapPlan.DeriveAudioWavPaths(string) not found."
            );
        return (IReadOnlyList<string>)(
            method.Invoke(null, new object[] { tempVideoPath })
            ?? throw new InvalidOperationException("DeriveAudioWavPaths returned null.")
        );
    }
}

// ---------------------------------------------------------------------------
// 8) Native encoder diagnostics remain bounded in terminal records.
//    chunk-fed tail. A single hostile line must never expand retained memory.
// ---------------------------------------------------------------------------
file static class RecordingOperationContractTests
{
    private static readonly Type OperationType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingOperation"
    );
    private static readonly Type CompletionType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingCompletion"
    );
    private static readonly Type SourceType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.CombatReplayPlaybackSource"
    );
    private static readonly Type AudioStatusType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioStatus"
    );
    private static readonly Type MetadataStatusType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoMetadataStatus"
    );
    private static readonly Type ReasonType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingReasonCode"
    );
    private static readonly Type LifecycleType = TestReflection.RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingLifecycle"
    );

    public static void Run()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "bpp-recording-operation-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            using var logs = new StructuredLogCapture();
            var fullPath = Path.Combine(root, "full.mp4");
            File.WriteAllBytes(fullPath, new byte[] { 1, 2, 3, 4 });
            var success = CreateOperation("recording-success-00000001");
            TestReflection.Assert(
                Complete(success, Completion(fullPath, "Full", "Complete", "Completed")),
                "A full artifact should close its operation."
            );
            logs.AssertSingle(LogLevel.Info, "event=combat_replay.video_recording.succeeded");

            logs.Clear();
            var silentPath = Path.Combine(root, "silent.mp4");
            File.WriteAllBytes(silentPath, new byte[] { 5 });
            var degraded = CreateOperation("recording-degraded-00000002");
            TestReflection.Assert(
                Complete(
                    degraded,
                    Completion(silentPath, "Silent", "Complete", "AudioUnavailable")
                ),
                "A preserved silent artifact should close as degraded."
            );
            logs.AssertSingle(LogLevel.Warning, "event=combat_replay.video_recording.degraded");

            logs.Clear();
            var missing = CreateOperation("recording-missing-00000003");
            TestReflection.Assert(
                Complete(
                    missing,
                    Completion(Path.Combine(root, "missing.mp4"), "Full", "Complete", "Completed")
                ),
                "A missing artifact should still close the requested operation."
            );
            logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");

            logs.Clear();
            var racing = CreateOperation("recording-race-00000004");
            var racePath = Path.Combine(root, "race.mp4");
            File.WriteAllBytes(racePath, new byte[] { 9 });
            var completion = Completion(racePath, "Full", "Complete", "Completed");
            var winners = 0;
            Parallel.For(
                0,
                64,
                _ =>
                {
                    if (Complete(racing, completion))
                        Interlocked.Increment(ref winners);
                }
            );
            TestReflection.Assert(winners == 1, "Exactly one concurrent terminal may win.");
            logs.AssertSingle(LogLevel.Info, "event=combat_replay.video_recording.succeeded");
            TestReflection.Assert(
                !logs.Joined.Contains("recording-race-00000004", StringComparison.Ordinal),
                "Rendered correlation must not expose the full recording id."
            );

            ShutdownSweepClosesOrphanOnce(root, logs);
            TrackedShutdownSweepRejectsLateCompletion(root, logs);
            LifecycleScenarioMatrix(root, logs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void LifecycleScenarioMatrix(string root, StructuredLogCapture logs)
    {
        foreach (
            var reason in new[]
            {
                "UnsupportedPlatform",
                "OutputPathUnavailable",
                "InvalidDimensions",
            }
        )
        {
            logs.Clear();
            var (lifecycle, operation) = StartLifecycle("preflight-" + reason);
            InvokeLifecycle(
                lifecycle,
                "CompletePreflight",
                operation,
                Enum.Parse(ReasonType, reason),
                null,
                null
            );
            logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");
            TestReflection.Assert(
                !CompleteThroughLifecycle(
                    lifecycle,
                    operation,
                    Completion(
                        Path.Combine(root, reason + ".late.mp4"),
                        "Full",
                        "Complete",
                        "Completed"
                    )
                ),
                $"Late completion must lose after {reason}."
            );
        }

        foreach (
            var scenario in new[]
            {
                ("BeginException", "", "Failed", "Unavailable", LogLevel.Error),
                ("EncoderWriterFailed", "", "Full", "Complete", LogLevel.Error),
                ("EncoderTimeout", "", "Full", "Complete", LogLevel.Error),
                ("EncoderNonZeroExit", "", "Full", "Complete", LogLevel.Error),
                ("Aborted", "", "Failed", "Unavailable", LogLevel.Error),
                ("Superseded", "superseded.mp4", "Silent", "Complete", LogLevel.Warning),
                ("AudioUnavailable", "silent.mp4", "Silent", "Complete", LogLevel.Warning),
                ("AudioCaptureFailed", "audio-start.mp4", "Silent", "Complete", LogLevel.Warning),
                ("AudioStopFailed", "audio-stop.mp4", "Failed", "Complete", LogLevel.Warning),
                ("MetadataFailed", "metadata.mp4", "Full", "Failed", LogLevel.Warning),
            }
        )
        {
            logs.Clear();
            var (lifecycle, operation) = StartLifecycle("terminal-" + scenario.Item1);
            var path = string.IsNullOrEmpty(scenario.Item2)
                ? Path.Combine(root, scenario.Item1 + ".missing.mp4")
                : Path.Combine(root, scenario.Item2);
            if (!string.IsNullOrEmpty(scenario.Item2))
                File.WriteAllBytes(path, new byte[] { 1 });
            var completion = Completion(path, scenario.Item3, scenario.Item4, scenario.Item1);
            TestReflection.Assert(
                CompleteThroughLifecycle(lifecycle, operation, completion),
                $"{scenario.Item1} should close its lifecycle."
            );
            logs.AssertSingle(
                scenario.Item5,
                scenario.Item5 == LogLevel.Warning
                    ? "event=combat_replay.video_recording.degraded"
                    : "event=combat_replay.video_recording.failed"
            );
            TestReflection.Assert(
                !CompleteThroughLifecycle(lifecycle, operation, completion),
                $"{scenario.Item1} must remain one-shot."
            );
        }

        logs.Clear();
        var (successLifecycle, successOperation) = StartLifecycle("resolved-success");
        var successPath = Path.Combine(root, "resolved-success.mp4");
        File.WriteAllBytes(successPath, new byte[] { 1, 2 });
        TestReflection.Assert(
            CompleteResolved(
                successLifecycle,
                successOperation,
                Capture("Completed", "Completed"),
                Mux("Muxed", "Muxed", successPath, 2),
                "Full",
                "Complete",
                null
            ),
            "Full resolved mux should complete."
        );
        logs.AssertSingle(LogLevel.Info, "event=combat_replay.video_recording.succeeded");

        logs.Clear();
        var (fallbackLifecycle, fallbackOperation) = StartLifecycle("resolved-fallback");
        var fallbackPath = Path.Combine(root, "resolved-fallback.mp4");
        File.WriteAllBytes(fallbackPath, new byte[] { 3 });
        TestReflection.Assert(
            CompleteResolved(
                fallbackLifecycle,
                fallbackOperation,
                Capture("Completed", "Completed"),
                Mux("FellBackToSilent", "NativeMuxFailed", fallbackPath, 1),
                "Silent",
                "Complete",
                null
            ),
            "Mux fallback should close degraded."
        );
        logs.AssertSingle(LogLevel.Warning, "event=combat_replay.video_recording.degraded");

        foreach (
            var resolved in new[]
            {
                (
                    "audio-unavailable",
                    "FellBackToSilent",
                    "NoAudio",
                    "Silent",
                    "Complete",
                    "AudioUnavailable"
                ),
                ("audio-stop-failed", "Muxed", "Muxed", "Failed", "Complete", "AudioStopFailed"),
                ("metadata-failed", "Muxed", "Muxed", "Full", "Failed", (string?)null),
            }
        )
        {
            logs.Clear();
            var (lifecycle, operation) = StartLifecycle("resolved-" + resolved.Item1);
            var path = Path.Combine(root, "resolved-" + resolved.Item1 + ".mp4");
            File.WriteAllBytes(path, new byte[] { 5 });
            TestReflection.Assert(
                CompleteResolved(
                    lifecycle,
                    operation,
                    Capture("Completed", "Completed"),
                    Mux(resolved.Item2, resolved.Item3, path, 1),
                    resolved.Item4,
                    resolved.Item5,
                    resolved.Item6
                ),
                $"Resolved {resolved.Item1} should close degraded."
            );
            logs.AssertSingle(LogLevel.Warning, "event=combat_replay.video_recording.degraded");
        }

        logs.Clear();
        var (promotionLifecycle, promotionOperation) = StartLifecycle("promotion-failed");
        TestReflection.Assert(
            CompleteResolved(
                promotionLifecycle,
                promotionOperation,
                Capture("Completed", "Completed"),
                Mux("Failed", "PromotionFailed", Path.Combine(root, "promotion.missing.mp4"), 0),
                "Full",
                "Complete",
                null
            ),
            "Promotion failure should close failed."
        );
        logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");

        logs.Clear();
        var (callbackLifecycle, callbackOperation) = StartLifecycle("callback-failed");
        var callbackPath = Path.Combine(root, "callback.mp4");
        File.WriteAllBytes(callbackPath, new byte[] { 4 });
        InvokeLifecycle(
            callbackLifecycle,
            "CompleteMuxCallbackFailure",
            callbackOperation,
            Capture("Completed", "Completed"),
            Mux("Muxed", "Muxed", callbackPath, 1),
            Enum.Parse(AudioStatusType, "Full"),
            Enum.Parse(MetadataStatusType, "Complete"),
            new InvalidOperationException("callback")
        );
        logs.AssertSingle(LogLevel.Warning, "event=combat_replay.video_recording.degraded");
        InvokeLifecycle(
            callbackLifecycle,
            "CompletePending",
            Enum.Parse(ReasonType, "ShutdownTimeout")
        );
        logs.AssertSingle(LogLevel.Warning, "event=combat_replay.video_recording.degraded");

        logs.Clear();
        var (shutdownLifecycle, shutdownOperation) = StartLifecycle("shutdown-pending");
        InvokeLifecycle(
            shutdownLifecycle,
            "CompletePending",
            Enum.Parse(ReasonType, "ShutdownTimeout")
        );
        logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");
        var shutdownLatePath = Path.Combine(root, "shutdown-late.mp4");
        File.WriteAllBytes(shutdownLatePath, new byte[] { 6 });
        TestReflection.Assert(
            !CompleteResolved(
                shutdownLifecycle,
                shutdownOperation,
                Capture("Completed", "Completed"),
                Mux("Muxed", "Muxed", shutdownLatePath, 1),
                "Full",
                "Complete",
                null
            ),
            "Late mux completion must lose after shutdown."
        );
        logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");
    }

    private static void ShutdownSweepClosesOrphanOnce(string root, StructuredLogCapture logs)
    {
        logs.Clear();
        var registryType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingOperationRegistry"
        );
        var registry = Activator.CreateInstance(registryType, nonPublic: true)!;
        var operation = CreateOperation("recording-callback-orphan-00000006");
        registryType
            .GetMethod(
                "Register",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(registry, new[] { operation });
        registryType
            .GetMethod(
                "CompletePending",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(registry, new[] { Enum.Parse(ReasonType, "ShutdownTimeout") });
        logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");
        TestReflection.Assert(
            logs.Joined.Contains("reason_code=shutdown_timeout", StringComparison.Ordinal),
            "A successfully drained but orphaned callback must close as shutdown timeout."
        );

        var latePath = Path.Combine(root, "late.mp4");
        File.WriteAllBytes(latePath, new byte[] { 1 });
        var tryComplete = registryType.GetMethod(
            "TryComplete",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        )!;
        TestReflection.Assert(
            !(bool)(
                tryComplete.Invoke(
                    registry,
                    new[] { operation, Completion(latePath, "Full", "Complete", "Completed") }
                ) ?? true
            ),
            "A late mux callback must lose after the shutdown sweep."
        );
        logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");
    }

    private static void TrackedShutdownSweepRejectsLateCompletion(
        string root,
        StructuredLogCapture logs
    )
    {
        logs.Clear();
        var registryType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingOperationRegistry"
        );
        var muxerType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer"
        );
        var registry = Activator.CreateInstance(registryType, nonPublic: true)!;
        var operation = CreateOperation("recording-tracked-shutdown-00000007");
        registryType
            .GetMethod(
                "Register",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(registry, new[] { operation });
        var tryComplete = registryType.GetMethod(
            "TryComplete",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        )!;
        var dispatch =
            muxerType.GetMethod(
                "DispatchTracked",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action) },
                modifiers: null
            ) ?? throw new InvalidOperationException("Tracked background dispatch seam not found.");
        using var release = new ManualResetEventSlim(false);
        var lateWon = 0;
        var latePath = Path.Combine(root, "tracked-shutdown-late.mp4");
        File.WriteAllBytes(latePath, new byte[] { 1 });
        var task = (Task)
            dispatch.Invoke(
                null,
                new object[]
                {
                    new Action(() =>
                    {
                        release.Wait();
                        if (
                            (bool)(
                                tryComplete.Invoke(
                                    registry,
                                    new[]
                                    {
                                        operation,
                                        Completion(latePath, "Full", "Complete", "Completed"),
                                    }
                                ) ?? false
                            )
                        )
                        {
                            Interlocked.Exchange(ref lateWon, 1);
                        }
                    }),
                }
            )!;

        var drained = (bool)
            muxerType
                .GetMethod(
                    "TryDrainPendingForShutdown",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                )!
                .Invoke(null, new object[] { TimeSpan.FromMilliseconds(10) })!;
        TestReflection.Assert(!drained, "Shutdown drain must observe the tracked finalize task.");
        registryType
            .GetMethod(
                "CompletePending",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(registry, new[] { Enum.Parse(ReasonType, "ShutdownTimeout") });
        release.Set();
        TestReflection.Assert(
            task.Wait(TimeSpan.FromSeconds(5)),
            "Tracked finalize task must exit."
        );
        TestReflection.Assert(lateWon == 0, "Late completion must lose after the shutdown sweep.");
        logs.AssertSingle(LogLevel.Error, "event=combat_replay.video_recording.failed");
    }

    private static (object Lifecycle, object Operation) StartLifecycle(string recordingId)
    {
        var factory = new Func<string>(() => recordingId);
        var ctor =
            LifecycleType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(Func<string>) },
                modifiers: null
            ) ?? throw new InvalidOperationException("Recording lifecycle constructor not found.");
        var lifecycle = ctor.Invoke(new object[] { factory });
        var start =
            LifecycleType.GetMethod(
                "Start",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Recording lifecycle Start not found.");
        var operation =
            start.Invoke(
                lifecycle,
                new[]
                {
                    "battle-lifecycle-00000001",
                    Enum.Parse(SourceType, "ImportedGhost"),
                    new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero),
                }
            ) ?? throw new InvalidOperationException("Lifecycle Start returned null.");
        return (lifecycle, operation);
    }

    private static bool CompleteThroughLifecycle(
        object lifecycle,
        object operation,
        object completion
    ) => (bool)(InvokeLifecycle(lifecycle, "TryComplete", operation, completion) ?? false);

    private static bool CompleteResolved(
        object lifecycle,
        object operation,
        object capture,
        object mux,
        string audioStatus,
        string metadataStatus,
        string? degradationReason
    ) =>
        (bool)(
            InvokeLifecycle(
                lifecycle,
                "CompleteResolved",
                operation,
                capture,
                mux,
                Enum.Parse(AudioStatusType, audioStatus),
                Enum.Parse(MetadataStatusType, metadataStatus),
                degradationReason == null ? null : Enum.Parse(ReasonType, degradationReason),
                null
            ) ?? false
        );

    private static object Capture(string status, string reason)
    {
        var type = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoCaptureResult"
        );
        var instance = Activator.CreateInstance(type, nonPublic: true)!;
        Set(instance, "VideoId", "video-lifecycle-00000001");
        Set(instance, "CapturedFrames", 30);
        Set(instance, "DroppedFrames", 2);
        Set(
            instance,
            "Status",
            Enum.Parse(
                TestReflection.RequireType(
                    "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoCaptureStatus"
                ),
                status
            )
        );
        Set(instance, "ReasonCode", Enum.Parse(ReasonType, reason));
        Set(instance, "EndedAtUtc", new DateTimeOffset(2026, 7, 13, 0, 0, 1, TimeSpan.Zero));
        return instance;
    }

    private static object Mux(string status, string reason, string path, long size)
    {
        var type = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer+MuxResult"
        );
        var statusType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer+MuxStatus"
        );
        var reasonType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer+MuxReasonCode"
        );
        var ctor = type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            .Single(candidate => candidate.GetParameters().Length == 7);
        return ctor.Invoke(
            new object?[]
            {
                Enum.Parse(statusType, status),
                path,
                size,
                Enum.Parse(reasonType, reason),
                null,
                null,
                null,
            }
        );
    }

    private static object? InvokeLifecycle(object lifecycle, string name, params object?[] args)
    {
        var method = LifecycleType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.Name == name && candidate.GetParameters().Length == args.Length
            );
        return method.Invoke(lifecycle, args);
    }

    private static object CreateOperation(string recordingId)
    {
        var ctor =
            OperationType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(string), typeof(string), SourceType, typeof(DateTimeOffset) },
                modifiers: null
            ) ?? throw new InvalidOperationException("Recording operation constructor not found.");
        return ctor.Invoke(
            new[]
            {
                recordingId,
                "battle-private-00000001",
                Enum.Parse(SourceType, "ImportedGhost"),
                new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero),
            }
        );
    }

    private static object Completion(
        string path,
        string audioStatus,
        string metadataStatus,
        string reason
    )
    {
        var completion =
            Activator.CreateInstance(CompletionType, nonPublic: true)
            ?? throw new InvalidOperationException("Recording completion should be constructible.");
        Set(completion, "FinalFilePath", path);
        Set(completion, "CapturedFrames", 30);
        Set(completion, "DroppedFrames", 0);
        Set(completion, "AudioStatus", Enum.Parse(AudioStatusType, audioStatus));
        Set(completion, "MetadataStatus", Enum.Parse(MetadataStatusType, metadataStatus));
        Set(completion, "ReasonCode", Enum.Parse(ReasonType, reason));
        return completion;
    }

    private static bool Complete(object operation, object completion)
    {
        var method =
            OperationType.GetMethod(
                "TryComplete",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                new[] { CompletionType },
                modifiers: null
            ) ?? throw new InvalidOperationException("TryComplete not found.");
        return (bool)(method.Invoke(operation, new[] { completion }) ?? false);
    }

    private static void Set(object instance, string name, object? value)
    {
        var property =
            instance
                .GetType()
                .GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                )
            ?? throw new InvalidOperationException($"Property not found: {name}");
        property.SetValue(instance, value);
    }

    private sealed class StructuredLogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("CombatReplayAudioVideo.Tests");
        private readonly List<LogEventArgs> _events = new();

        internal StructuredLogCapture()
        {
            _source.LogEvent += OnLogEvent;
            var bppLog = TestReflection.RequireType("BazaarPlusPlus.Infrastructure.BppLog");
            var install =
                bppLog.GetMethod(
                    "Install",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                ) ?? throw new InvalidOperationException("BppLog.Install not found.");
            install.Invoke(null, new object[] { _source });
        }

        internal string Joined => string.Join("\n", _events.Select(evt => evt.Data?.ToString()));

        internal void Clear() => _events.Clear();

        internal void AssertSingle(LogLevel level, string eventToken)
        {
            var matches = _events.Where(evt =>
                evt.Level == level
                && evt.Data?.ToString()?.Contains(eventToken, StringComparison.Ordinal) == true
            );
            TestReflection.Assert(
                matches.Count() == 1,
                $"Expected exactly one {level} {eventToken}; records={Joined}"
            );
            TestReflection.Assert(
                _events.Count(evt =>
                    evt.Data?.ToString()
                        ?.Contains("event=combat_replay.video_recording.", StringComparison.Ordinal)
                    == true
                ) == 1,
                $"Expected one authoritative recording terminal; records={Joined}"
            );
        }

        public void Dispose()
        {
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args) => _events.Add(args);
    }
}

// ---------------------------------------------------------------------------
// 10) Locked media event vocabulary: all eleven definitions are directly
//     discoverable, unique, valid, and preserve their exact ordered schemas.
// ---------------------------------------------------------------------------
file static class MediaEventCatalogTests
{
    private static readonly IReadOnlyDictionary<string, string> Expected = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["combat_replay.video_recording.succeeded"] = TerminalSchema,
        ["combat_replay.video_recording.degraded"] = TerminalSchema,
        ["combat_replay.video_recording.failed"] =
            TerminalSchema + "|exit_code:Low:None|stderr_tail:High:None",
        ["combat_replay.audio_capture.started"] =
            "recording_id:High:Short|backend:Low:None|sample_rate_hz:High:None|channels:Low:None|sample_format:Low:None",
        ["combat_replay.audio_capture.completed"] =
            "recording_id:High:Short|backend:Low:None|usable:Low:None|sample_float_count:High:None|rms_db:High:None|peak_db:High:None|size_bytes:High:None|wav_path:High:None",
        ["combat_replay.video_recording.lifecycle_observed"] =
            "stage:Low:None|recording_id:High:Short|battle_id:High:Short|pending_count:High:None",
        ["combat_replay.video_capture.stats_observed"] =
            "recording_id:High:Short|stage:Low:None|width:High:None|height:High:None|fps:Low:None|captured_frames:High:None|repeated_frames:High:None|dropped_frames:High:None|duration_ms:High:None|size_bytes:High:None|output_path:High:None|codec:Low:None|rate_control:Low:None|frame_bytes:High:None|pool_capacity:Low:None|queue_capacity:Low:None|pool_payload_bytes:High:None|pool_budget_exceeded:Low:None|cfr_copy_p95_us:High:None|staging_buffer_bytes:High:None|render_texture_estimated_bytes:High:None",
        ["combat_replay.video_capture.native_pipeline_observed"] =
            "recording_id:High:Short|stage:Low:None|backpressure_dropped_frames:High:None|dropped_frames:High:None|lease_misses:High:None|enqueue_rejects:High:None|pacer_resync_dropped_frames:High:None|max_in_flight:Low:None|native_frames_written:High:None|render_frame_p50_us:High:None|render_frame_p95_us:High:None|render_frame_p99_us:High:None|texture_copy_p50_us:High:None|texture_copy_p95_us:High:None|texture_copy_p99_us:High:None|battle_id:High:Short|encoder_name:Low:None",
        ["combat_replay.video_recording.cleanup_failed"] =
            "recording_id:High:Short|stage:Low:None|path:High:None",
        ["combat_replay.video_mux.diagnostic_observed"] =
            "recording_id:High:Short|stage:Low:None|reason_code:Low:None|path:High:None|pending_count:High:None",
        ["combat_replay.video_artifact.maintenance_observed"] =
            "status:Low:None|metadata_count:High:None|present_count:High:None|missing_count:High:None|unknown_mp4_count:High:None|temp_candidate_count:High:None|temp_deleted_count:High:None|failed_count:High:None|unmanaged_count:High:None",
    };

    private const string TerminalSchema =
        "recording_id:High:Short|battle_id:High:Short|source:Low:None|reason_code:Low:None|duration_ms:High:None|captured_frames:High:None|dropped_frames:High:None|size_bytes:High:None|audio_status:Low:None|metadata_status:Low:None|output_path:High:None";

    public static void Run()
    {
        var source = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.CombatReplayVideoLogEvents"
        );
        var eventDefinition = TestReflection.RequireType(
            "BazaarPlusPlus.Infrastructure.Logging.BppLogEventDefinition"
        );
        var direct = source
            .GetFields(
                BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            )
            .Where(field => field.FieldType == eventDefinition)
            .Select(field => field.GetValue(null)!)
            .ToArray();
        TestReflection.Assert(
            direct.Length == 11,
            $"Expected 11 media events, got {direct.Length}."
        );

        var actual = direct.ToDictionary(EventId, Schema, StringComparer.Ordinal);
        TestReflection.Assert(actual.Count == direct.Length, "Media event IDs must be unique.");
        foreach (var expected in Expected)
        {
            TestReflection.Assert(
                actual.TryGetValue(expected.Key, out var schema) && schema == expected.Value,
                $"Schema drift for {expected.Key}: {schema ?? "<missing>"}"
            );
        }

        var catalogType = TestReflection.RequireType(
            "BazaarPlusPlus.Infrastructure.Logging.BppLogEventCatalog"
        );
        var discover =
            catalogType.GetMethod(
                "Discover",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("BppLogEventCatalog.Discover not found.");
        var catalog = discover.Invoke(null, new object[] { source.Assembly })!;
        var discovered = (
            (System.Collections.IEnumerable)
                TestReflection.GetProp(catalogType, catalog, "Definitions")!
        )
            .Cast<object>()
            .Select(EventId)
            .ToHashSet(StringComparer.Ordinal);
        TestReflection.Assert(
            Expected.Keys.All(discovered.Contains),
            "Every media definition must be discoverable from the assembly catalog."
        );

        var fromDefinitions =
            catalogType.GetMethod(
                "FromDefinitions",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?? throw new InvalidOperationException("BppLogEventCatalog.FromDefinitions not found.");
        var typed = Array.CreateInstance(eventDefinition, direct.Length);
        for (var index = 0; index < direct.Length; index++)
            typed.SetValue(direct[index], index);
        var directCatalog = fromDefinitions.Invoke(null, new object[] { typed })!;
        var validate =
            catalogType.GetMethod(
                "Validate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("BppLogEventCatalog.Validate not found.");
        var validation = validate.Invoke(directCatalog, Array.Empty<object>())!;
        TestReflection.Assert(
            (bool)(
                validation
                    .GetType()
                    .GetProperty(
                        "IsValid",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    )!
                    .GetValue(validation)
                ?? false
            ),
            "The locked media catalog must pass governance validation."
        );
    }

    private static string EventId(object definition) =>
        (string)(
            TestReflection.GetProp(definition.GetType(), definition, "EventId") ?? string.Empty
        );

    private static string Schema(object definition)
    {
        var fields = (System.Collections.IEnumerable)
            TestReflection.GetProp(definition.GetType(), definition, "Fields")!;
        return string.Join(
            "|",
            fields
                .Cast<object>()
                .Select(field =>
                    $"{TestReflection.GetProp(field.GetType(), field, "Name")}:{TestReflection.GetProp(field.GetType(), field, "Cardinality")}:{TestReflection.GetProp(field.GetType(), field, "Correlation")}"
                )
        );
    }
}

// ---------------------------------------------------------------------------
// 11) Audio teardown timeout is a typed result, and never leaves a timed-out
//     tap eligible for muxing.
// ---------------------------------------------------------------------------
file static class AudioStopTimeoutTests
{
    public static void Run()
    {
        using var release = new ManualResetEventSlim(false);
        var thread = new Thread(() => release.Wait()) { IsBackground = true };
        thread.Start();
        try
        {
            var joiner = TestReflection.RequireType(
                "BazaarPlusPlus.Game.CombatReplay.Audio.ReplayAudioCaptureThreadJoiner"
            );
            var method =
                joiner.GetMethod(
                    "TryJoin",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                ) ?? throw new InvalidOperationException("Replay audio join helper not found.");
            var args = new object?[] { thread, 20, null };
            TestReflection.Assert(
                !(bool)(method.Invoke(null, args) ?? true),
                "A live capture thread must fail its fixed bounded join."
            );
            TestReflection.Assert(
                args[2] is TimeoutException timeout
                    && timeout.Message
                        == "Replay audio capture thread did not stop within the fixed timeout.",
                "Join timeout must produce the fixed typed TimeoutException."
            );
        }
        finally
        {
            release.Set();
            thread.Join();
        }
    }
}

// ---------------------------------------------------------------------------
// 12) Resolved final metadata: the persisted recorder status must agree with
//     the verified artifact and terminal capture outcome.
// ---------------------------------------------------------------------------
file static class RecorderIntegrationContractTests
{
    public static void Run()
    {
        var resolutionType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoMetadataResolution"
        );
        var captureStatusType = TestReflection.RequireType(
            "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoCaptureStatus"
        );
        var statusMethod =
            resolutionType.GetMethod(
                "ResolvePersistedStatus",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Resolved metadata status helper not found.");
        string Status(string capture, bool resolved, long size) =>
            (string)(
                statusMethod.Invoke(
                    null,
                    new[] { Enum.Parse(captureStatusType, capture), resolved, (object)size }
                ) ?? string.Empty
            );
        TestReflection.Assert(
            Status("Completed", true, 1) == "COMPLETED",
            "Resolved artifact should persist completed."
        );
        TestReflection.Assert(
            Status("Completed", false, 1) == "FAILED",
            "Failed mux/promotion must persist failed despite bytes."
        );
        TestReflection.Assert(
            Status("Completed", true, 0) == "FAILED",
            "Missing final artifact must persist failed."
        );
        TestReflection.Assert(
            Status("Failed", true, 1) == "FAILED",
            "Failed capture must persist failed."
        );
    }
}

// ---------------------------------------------------------------------------
// Shared reflection helpers (file-scoped types cannot see top-level locals).
// ---------------------------------------------------------------------------
file static class TestReflection
{
    public static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static Type RequireType(string fullName) =>
        Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");

    public static object? GetProp(Type type, object instance, string name)
    {
        var prop =
            type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");
        return prop.GetValue(instance);
    }

    public static object? GetField(Type type, object instance, string name)
    {
        var field =
            type.GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException($"Field not found: {type.FullName}.{name}");
        return field.GetValue(instance);
    }

    public static object? Invoke(
        Type type,
        object instance,
        string name,
        Type[] paramTypes,
        object[] args
    )
    {
        var method =
            type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: paramTypes,
                modifiers: null
            )
            ?? throw new InvalidOperationException(
                $"Method not found: {type.FullName}.{name}({paramTypes.Length} args)"
            );
        return method.Invoke(instance, args);
    }
}
