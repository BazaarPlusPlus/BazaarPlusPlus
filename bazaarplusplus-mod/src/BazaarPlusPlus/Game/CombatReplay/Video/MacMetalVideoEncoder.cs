#nullable enable
using System.Runtime.InteropServices;
using System.Text;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Local macOS experiment: Unity copies each captured frame directly into an IOSurface-backed
/// Metal texture. VideoToolbox consumes that pixel buffer in-process, so no full-frame GPU
/// readback or raw-video pipe is involved.
/// </summary>
internal sealed class MacMetalVideoEncoder : IReplayVideoEncoder
{
    internal const string NativeLibrary = "GfxPluginBppReplayVideoToolbox";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStats
    {
        internal int SubmittedFrames;
        internal int AppendedFrames;
        internal int AcquireMisses;
        internal int NotReadyDrops;
        internal int EncodeErrors;
        internal int MaxInFlight;
    }

    internal readonly struct FrameLease
    {
        internal FrameLease(int slotIndex)
        {
            SlotIndex = slotIndex;
        }

        internal int SlotIndex { get; }
    }

    private readonly string _recordingId;
    private readonly string _outputFilePath;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly int _targetBitrateKbps;
    private readonly object _nativeSync = new();
    private IntPtr _handle;
    private int _slotCount;
    private bool _started;
    private bool _sealed;
    private bool _finished;
    private bool _disposed;
    private ReplayVideoEncoderFailureReasonCode _failureReasonCode;
    private IntPtr _renderEventFunction;

    internal MacMetalVideoEncoder(
        string recordingId,
        string outputFilePath,
        int width,
        int height,
        int fps,
        int targetBitrateKbps
    )
    {
        _recordingId = recordingId;
        _outputFilePath = outputFilePath;
        _width = width;
        _height = height;
        _fps = fps;
        _targetBitrateKbps = targetBitrateKbps;
    }

    public bool WriterFailed
    {
        get
        {
            lock (_nativeSync)
            {
                return _failureReasonCode != ReplayVideoEncoderFailureReasonCode.None
                    || (_handle != IntPtr.Zero && BppVtIsFailed(_handle) != 0);
            }
        }
    }

    public ReplayVideoEncoderFailureReasonCode FailureReasonCode =>
        _failureReasonCode == ReplayVideoEncoderFailureReasonCode.None
            ? ReplayVideoEncoderFailureReasonCode.WriterCrashed
            : _failureReasonCode;

    internal int SlotCount => _slotCount;

    internal IntPtr RenderEventFunction => _renderEventFunction;

    internal static bool TryGetAvailability(out string? reason)
    {
        if (ReplayVideoEncoderProfile.DetectPlatform() != VideoEncoderPlatform.MacOS)
        {
            reason = "The native replay recorder is only available on macOS.";
            return false;
        }

        try
        {
            if (BppVtHasUnityMetalInterface() == 0)
            {
                reason = "The native replay recorder was not loaded by Unity's Metal renderer.";
                return false;
            }

            if (BppVtCanMuxAudio() == 0)
            {
                reason = "The native replay recorder cannot create an AAC audio track.";
                return false;
            }

            reason = null;
            return true;
        }
        catch (Exception ex)
            when (ex
                    is DllNotFoundException
                        or EntryPointNotFoundException
                        or BadImageFormatException
            )
        {
            reason = ex.Message;
            return false;
        }
    }

    internal void Start()
    {
        if (_started)
            throw new InvalidOperationException("Metal encoder is already started.");
        if (BppVtHasUnityMetalInterface() == 0)
        {
            throw new InvalidOperationException(
                "The replay encoder native plugin was not preloaded by Unity's Metal renderer."
            );
        }

        _renderEventFunction = BppVtGetRenderEventFunc();
        if (_renderEventFunction == IntPtr.Zero)
            throw new InvalidOperationException("The native Metal render callback is unavailable.");

        var result = BppVtCreate(
            _outputFilePath,
            _width,
            _height,
            _fps,
            checked(_targetBitrateKbps * 1000),
            out _handle
        );
        if (result != 0 || _handle == IntPtr.Zero)
        {
            var error = ReadNativeError();
            if (_handle != IntPtr.Zero)
                BppVtDestroy(_handle);
            _handle = IntPtr.Zero;
            throw new InvalidOperationException(
                $"Could not start the Metal VideoToolbox encoder ({result}): {error}"
            );
        }

        try
        {
            var slotCount = BppVtGetSlotCount(_handle);
            if (slotCount <= 1)
                throw new InvalidOperationException(
                    $"Metal VideoToolbox encoder returned invalid slot count {slotCount}."
                );

            _slotCount = slotCount;
            _started = true;
        }
        catch
        {
            SealCapture();
            BppVtDestroy(_handle);
            _handle = IntPtr.Zero;
            throw;
        }
    }

    internal bool TryAcquireFrame(out FrameLease lease)
    {
        lock (_nativeSync)
        {
            if (!_started || _sealed || WriterFailed || _slotCount <= 0)
            {
                lease = default;
                return false;
            }

            if (BppVtAcquireSlot(_handle, out var slotIndex) == 0)
            {
                lease = default;
                return false;
            }
            if (slotIndex < 0 || slotIndex >= _slotCount)
            {
                BppVtReleaseSlot(_handle, slotIndex);
                lease = default;
                return false;
            }

            lease = new FrameLease(slotIndex);
            return true;
        }
    }

    internal bool TryPrepareRenderEvent(
        FrameLease lease,
        long firstFrameIndex,
        int frameCount,
        out IntPtr eventData
    )
    {
        lock (_nativeSync)
        {
            if (_sealed || _handle == IntPtr.Zero || WriterFailed)
            {
                eventData = IntPtr.Zero;
                return false;
            }

            eventData = BppVtPrepareRenderEvent(
                _handle,
                lease.SlotIndex,
                firstFrameIndex,
                frameCount
            );
            return eventData != IntPtr.Zero;
        }
    }

    internal void CommitRenderEvent(IntPtr eventData)
    {
        if (eventData != IntPtr.Zero)
            BppVtCommitRenderEvent(eventData);
    }

    internal void CancelRenderEvent(IntPtr eventData)
    {
        if (eventData != IntPtr.Zero)
            BppVtCancelRenderEvent(eventData);
    }

    internal void DiscardRenderEvent(IntPtr eventData)
    {
        if (eventData != IntPtr.Zero)
            BppVtDiscardRenderEvent(eventData);
    }

    internal void ReleaseFrame(FrameLease lease)
    {
        lock (_nativeSync)
        {
            if (_handle != IntPtr.Zero)
                BppVtReleaseSlot(_handle, lease.SlotIndex);
        }
    }

    internal void SealCapture()
    {
        if (_sealed)
            return;
        _sealed = true;
    }

    public void SignalEndOfStream()
    {
        // VideoToolbox is drained by WaitForCompletion after Unity has sealed all GPU requests.
    }

    public ReplayVideoEncoderCompletionOutcome WaitForCompletion(TimeSpan timeout)
    {
        lock (_nativeSync)
        {
            if (_handle == IntPtr.Zero)
            {
                return ReplayVideoEncoderCompletionOutcome.Failure(
                    ReplayVideoEncoderFailureReasonCode.WriterCrashed,
                    null,
                    "Metal VideoToolbox encoder handle is unavailable."
                );
            }
            if (_finished)
                return WriterFailed
                    ? NativeFailureOutcome()
                    : ReplayVideoEncoderCompletionOutcome.Success(ReadNativeError());

            var timeoutMs = (int)Math.Max(1, Math.Min(int.MaxValue, timeout.TotalMilliseconds));
            var succeeded = BppVtFinish(_handle, timeoutMs) != 0;
            _finished = true;
            LogStats();
            if (succeeded && !WriterFailed)
                return ReplayVideoEncoderCompletionOutcome.Success(ReadNativeError());

            _failureReasonCode = ReplayVideoEncoderFailureReasonCode.WriterCrashed;
            return NativeFailureOutcome();
        }
    }

    public void Dispose()
    {
        lock (_nativeSync)
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_handle != IntPtr.Zero)
            {
                BppVtDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    private ReplayVideoEncoderCompletionOutcome NativeFailureOutcome() =>
        ReplayVideoEncoderCompletionOutcome.Failure(FailureReasonCode, null, ReadNativeError());

    private string ReadNativeError()
    {
        if (_handle == IntPtr.Zero)
            return "Native encoder initialization failed.";

        var buffer = new StringBuilder(2048);
        BppVtCopyError(_handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private void LogStats()
    {
        if (_handle == IntPtr.Zero)
            return;

        BppVtGetStats(_handle, out var stats);
        BppLog.DebugEvent(
            CombatReplayVideoLogEvents.VideoCaptureNativePipelineObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.NativeStatsRecordingId.Bind(_recordingId),
                    CombatReplayVideoLogEvents.NativeStatsStage.Bind("metal_vt_drained"),
                    CombatReplayVideoLogEvents.NativeStatsFramesWritten.Bind(stats.AppendedFrames),
                    CombatReplayVideoLogEvents.NativeStatsLeaseMisses.Bind(stats.AcquireMisses),
                    CombatReplayVideoLogEvents.NativeStatsEnqueueRejects.Bind(
                        stats.NotReadyDrops + stats.EncodeErrors
                    ),
                    CombatReplayVideoLogEvents.NativeStatsMaxInFlight.Bind(stats.MaxInFlight),
                ]
        );
    }

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppVtHasUnityMetalInterface();

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppVtCanMuxAudio();

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BppVtGetRenderEventFunc();

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppVtCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string outputPath,
        int width,
        int height,
        int fps,
        int bitrateBitsPerSecond,
        out IntPtr handle
    );

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppVtGetSlotCount(IntPtr handle);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppVtAcquireSlot(IntPtr handle, out int slotIndex);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BppVtPrepareRenderEvent(
        IntPtr handle,
        int slotIndex,
        long firstFrameIndex,
        int frameCount
    );

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppVtCommitRenderEvent(IntPtr eventData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppVtCancelRenderEvent(IntPtr eventData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppVtDiscardRenderEvent(IntPtr eventData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppVtReleaseSlot(IntPtr handle, int slotIndex);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppVtFinish(IntPtr handle, int timeoutMs);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppVtDestroy(IntPtr handle);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppVtIsFailed(IntPtr handle);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppVtGetStats(IntPtr handle, out NativeStats stats);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int BppVtCopyError(IntPtr handle, StringBuilder buffer, int capacity);
}
