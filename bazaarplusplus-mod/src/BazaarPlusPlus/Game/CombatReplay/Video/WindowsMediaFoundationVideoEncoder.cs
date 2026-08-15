#nullable enable
using System.Runtime.InteropServices;
using System.Text;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Windows D3D11 replay encoder. Unity owns the persistent source texture while the native plugin
/// converts it into an NV12 surface pool and submits those surfaces directly to a verified
/// hardware Media Foundation H.264 transform.
/// </summary>
internal sealed class WindowsMediaFoundationVideoEncoder : IReplayVideoEncoder
{
    internal const string NativeLibrary = "GfxPluginBppReplayMediaFoundation";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStats
    {
        internal int SubmittedFrames;
        internal int WrittenFrames;
        internal int AcquireMisses;
        internal int EnqueueRejects;
        internal int EncodeErrors;
        internal int MaxInFlight;
    }

    internal readonly record struct FrameLease(int SlotIndex);

    private readonly string _recordingId;
    private readonly string _outputFilePath;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly int _targetBitrateKbps;
    private readonly object _nativeSync = new();
    private IntPtr _handle;
    private IntPtr _renderEventFunction;
    private int _slotCount;
    private bool _started;
    private bool _sealed;
    private bool _finished;
    private bool _disposed;
    private ReplayVideoEncoderFailureReasonCode _failureReasonCode;

    internal WindowsMediaFoundationVideoEncoder(
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
                    || (_handle != IntPtr.Zero && BppMfIsFailed(_handle) != 0);
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
        if (ReplayVideoEncoderProfile.DetectPlatform() != VideoEncoderPlatform.Windows)
        {
            reason = "The Media Foundation replay recorder is only available on Windows.";
            return false;
        }

        try
        {
            if (BppMfHasUnityD3D11Interface() == 0)
            {
                reason = "The native replay plugin was not preloaded by Unity's D3D11 renderer.";
                return false;
            }
            if (BppMfCanMuxAudio() == 0)
            {
                reason = "The native replay plugin cannot create an AAC audio track.";
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

    internal void Start(IntPtr sourceTexture)
    {
        if (_started)
            throw new InvalidOperationException("Media Foundation encoder is already started.");
        if (sourceTexture == IntPtr.Zero)
            throw new InvalidOperationException("Unity returned an empty D3D11 capture texture.");
        if (BppMfHasUnityD3D11Interface() == 0)
        {
            throw new InvalidOperationException(
                "The replay encoder plugin was not preloaded by Unity's D3D11 renderer."
            );
        }

        _renderEventFunction = BppMfGetRenderEventFunc();
        if (_renderEventFunction == IntPtr.Zero)
            throw new InvalidOperationException("The native D3D11 render callback is unavailable.");

        var result = BppMfCreate(
            _outputFilePath,
            sourceTexture,
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
                BppMfDestroy(_handle);
            _handle = IntPtr.Zero;
            throw new InvalidOperationException(
                $"Could not start the D3D11 Media Foundation encoder ({result}): {error}"
            );
        }

        _slotCount = BppMfGetSlotCount(_handle);
        if (_slotCount <= 1)
        {
            throw new InvalidOperationException(
                $"Native encoder returned invalid slot count {_slotCount}."
            );
        }
        _started = true;

        BppLog.DebugEvent(
            CombatReplayVideoLogEvents.VideoCaptureNativePipelineObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.NativeStatsRecordingId.Bind(_recordingId),
                    CombatReplayVideoLogEvents.NativeStatsStage.Bind("d3d11_mf_started"),
                    CombatReplayVideoLogEvents.NativeStatsEncoderName.Bind(ReadEncoderName()),
                    CombatReplayVideoLogEvents.NativeStatsFramesWritten.Bind(0),
                    CombatReplayVideoLogEvents.NativeStatsLeaseMisses.Bind(0),
                    CombatReplayVideoLogEvents.NativeStatsEnqueueRejects.Bind(0),
                    CombatReplayVideoLogEvents.NativeStatsMaxInFlight.Bind(0),
                ]
        );
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
            if (BppMfAcquireSlot(_handle, out var slotIndex) == 0)
            {
                lease = default;
                return false;
            }
            if (slotIndex < 0 || slotIndex >= _slotCount)
            {
                BppMfReleaseSlot(_handle, slotIndex);
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
            eventData = BppMfPrepareRenderEvent(
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
            BppMfCommitRenderEvent(eventData);
    }

    internal void CancelRenderEvent(IntPtr eventData)
    {
        if (eventData != IntPtr.Zero)
            BppMfCancelRenderEvent(eventData);
    }

    internal void DiscardRenderEvent(IntPtr eventData)
    {
        if (eventData != IntPtr.Zero)
            BppMfDiscardRenderEvent(eventData);
    }

    internal void ReleaseFrame(FrameLease lease)
    {
        lock (_nativeSync)
        {
            if (_handle != IntPtr.Zero)
                BppMfReleaseSlot(_handle, lease.SlotIndex);
        }
    }

    internal void SealCapture() => _sealed = true;

    public void SignalEndOfStream() { }

    public ReplayVideoEncoderCompletionOutcome WaitForCompletion(TimeSpan timeout)
    {
        lock (_nativeSync)
        {
            if (_handle == IntPtr.Zero)
            {
                return ReplayVideoEncoderCompletionOutcome.Failure(
                    ReplayVideoEncoderFailureReasonCode.WriterCrashed,
                    null,
                    "Media Foundation encoder handle is unavailable."
                );
            }
            if (_finished)
                return WriterFailed
                    ? NativeFailureOutcome()
                    : ReplayVideoEncoderCompletionOutcome.Success(ReadNativeError());

            var timeoutMs = (int)Math.Max(1, Math.Min(int.MaxValue, timeout.TotalMilliseconds));
            var succeeded = BppMfFinish(_handle, timeoutMs) != 0;
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
                BppMfDestroy(_handle);
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
        BppMfCopyError(_handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private string ReadEncoderName()
    {
        var buffer = new StringBuilder(512);
        BppMfCopyEncoderName(_handle, buffer, buffer.Capacity);
        return buffer.Length == 0 ? "unknown_hardware_mft" : buffer.ToString();
    }

    private void LogStats()
    {
        BppMfGetStats(_handle, out var stats);
        BppLog.DebugEvent(
            CombatReplayVideoLogEvents.VideoCaptureNativePipelineObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.NativeStatsRecordingId.Bind(_recordingId),
                    CombatReplayVideoLogEvents.NativeStatsStage.Bind("d3d11_mf_drained"),
                    CombatReplayVideoLogEvents.NativeStatsEncoderName.Bind(ReadEncoderName()),
                    CombatReplayVideoLogEvents.NativeStatsFramesWritten.Bind(stats.WrittenFrames),
                    CombatReplayVideoLogEvents.NativeStatsLeaseMisses.Bind(stats.AcquireMisses),
                    CombatReplayVideoLogEvents.NativeStatsEnqueueRejects.Bind(
                        stats.EnqueueRejects + stats.EncodeErrors
                    ),
                    CombatReplayVideoLogEvents.NativeStatsMaxInFlight.Bind(stats.MaxInFlight),
                ]
        );
    }

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppMfHasUnityD3D11Interface();

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppMfCanMuxAudio();

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BppMfGetRenderEventFunc();

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppMfCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string outputPath,
        IntPtr sourceTexture,
        int width,
        int height,
        int fps,
        int bitrateBitsPerSecond,
        out IntPtr handle
    );

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppMfGetSlotCount(IntPtr handle);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppMfAcquireSlot(IntPtr handle, out int slotIndex);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BppMfPrepareRenderEvent(
        IntPtr handle,
        int slotIndex,
        long firstFrameIndex,
        int frameCount
    );

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppMfCommitRenderEvent(IntPtr eventData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppMfCancelRenderEvent(IntPtr eventData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppMfDiscardRenderEvent(IntPtr eventData);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppMfReleaseSlot(IntPtr handle, int slotIndex);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppMfFinish(IntPtr handle, int timeoutMs);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppMfDestroy(IntPtr handle);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int BppMfIsFailed(IntPtr handle);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BppMfGetStats(IntPtr handle, out NativeStats stats);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int BppMfCopyError(IntPtr handle, StringBuilder buffer, int capacity);

    [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int BppMfCopyEncoderName(
        IntPtr handle,
        StringBuilder buffer,
        int capacity
    );
}
