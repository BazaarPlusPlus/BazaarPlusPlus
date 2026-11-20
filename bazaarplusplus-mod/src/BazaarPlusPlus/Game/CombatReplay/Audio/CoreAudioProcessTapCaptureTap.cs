#nullable enable
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

// The native IOProc stays outside Mono because GC suspension, thread attach, or JIT on the real-time thread can drop audio.
internal sealed class CoreAudioProcessTapCaptureTap : IReplayAudioCaptureTap
{
    private readonly string _wavFilePath;

    private WavStreamWriter? _wav;
    private Thread? _thread;
    private volatile bool _running;
    private bool _stopped;
    private int _cleanupCompleted;
    private IntPtr _handle;

    private long _totalFloats;
    private double _sumSquares;
    private long _statSampleCount;
    private float _peakAbs;
    private int _sampleRate;
    private int _channels;
    private ReplayAudioFailureReasonCode _failureReason;
    private Exception? _failureException;

    public CoreAudioProcessTapCaptureTap(string wavFilePath)
    {
        _wavFilePath = wavFilePath ?? throw new ArgumentNullException(nameof(wavFilePath));
    }

    public bool IsCapturing { get; private set; }
    public string WavFilePath => _wavFilePath;
    public ReplayAudioBackend Backend => ReplayAudioBackend.CoreAudioProcessTap;
    public int SampleRateHz => _sampleRate;
    public int Channels => _channels;
    public string SampleFormat => "float32";
    public ReplayAudioFailureReasonCode FailureReason => _failureReason;
    public Exception? FailureException => _failureException;
    public bool CapturedAnySamples => Interlocked.Read(ref _totalFloats) > 0;
    public long CapturedSampleFloats => Interlocked.Read(ref _totalFloats);
    public double RmsAmplitude =>
        _statSampleCount > 0 ? Math.Sqrt(_sumSquares / _statSampleCount) : 0.0;
    public float PeakAmplitude => _peakAbs;

    public ReplayAudioCaptureStartOutcome TryStart()
    {
        try
        {
            _handle = BppMacAudio_Start(out var rate, out var ch);
            if (_handle == IntPtr.Zero)
            {
                _failureReason = ReplayAudioFailureReasonCode.BackendStartFailed;
                return ReplayAudioCaptureStartOutcome.Failure(_failureReason);
            }

            _sampleRate = rate;
            _channels = ch;

            _wav = new WavStreamWriter(_wavFilePath, rate, ch);

            _running = true;
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "BPP.CombatReplayAudio.CoreAudioTap",
            };
            _thread.Start();

            IsCapturing = true;
            return ReplayAudioCaptureStartOutcome.Success();
        }
        catch (Exception ex)
        {
            _failureReason = ReplayAudioFailureReasonCode.BackendStartFailed;
            _failureException = ex;
            SafeStopInternal();
            return ReplayAudioCaptureStartOutcome.Failure(_failureReason, ex);
        }
    }

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        _running = false;
        try
        {
            if (
                !ReplayAudioCaptureThreadJoiner.TryJoin(
                    _thread,
                    ReplayAudioCaptureThreadJoiner.StopTimeoutMs,
                    out var timeoutException
                )
            )
            {
                _failureReason = ReplayAudioFailureReasonCode.BackendStopFailed;
                _failureException = timeoutException;
                ScheduleDeferredCleanup(_thread);
                IsCapturing = false;
                return;
            }
        }
        catch (Exception ex)
        {
            _failureReason = ReplayAudioFailureReasonCode.BackendStopFailed;
            _failureException = ex;
        }

        CleanupResources(deleteWav: false);
        IsCapturing = false;
    }

    private void ScheduleDeferredCleanup(Thread? captureThread)
    {
        if (captureThread == null)
        {
            CleanupResources(deleteWav: true);
            return;
        }

        var cleanupThread = new Thread(() =>
        {
            try
            {
                while (!captureThread.Join(ReplayAudioCaptureThreadJoiner.StopTimeoutMs)) { }
            }
            finally
            {
                CleanupResources(deleteWav: true);
            }
        })
        {
            IsBackground = true,
            Name = "BPP.CombatReplayAudio.CoreAudioDeferredCleanup",
        };
        cleanupThread.Start();
    }

    private void CleanupResources(bool deleteWav)
    {
        if (Interlocked.Exchange(ref _cleanupCompleted, 1) != 0)
            return;

        if (_handle != IntPtr.Zero)
        {
            try
            {
                BppMacAudio_Stop(_handle);
            }
            catch (Exception ex)
            {
                _failureReason = ReplayAudioFailureReasonCode.BackendStopFailed;
                _failureException = ex;
            }
            _handle = IntPtr.Zero;
        }
        try
        {
            _wav?.Dispose();
        }
        catch (Exception ex)
        {
            _failureReason = ReplayAudioFailureReasonCode.WavCloseFailed;
            _failureException = ex;
        }
        _wav = null;
        if (deleteWav)
        {
            try
            {
                if (File.Exists(_wavFilePath))
                    File.Delete(_wavFilePath);
            }
            catch { }
        }
    }

    public void Dispose() => Stop();

    private void SafeStopInternal()
    {
        try
        {
            Stop();
        }
        catch { }
    }

    private void CaptureLoop()
    {
        var scratch = new float[16384];
        try
        {
            while (_running)
            {
                var n = BppMacAudio_Read(_handle, scratch, scratch.Length);
                if (n > 0)
                {
                    _wav!.WriteSamples(scratch, 0, n);
                    AccumulateStats(scratch, n);
                    Interlocked.Add(ref _totalFloats, n);
                }
                else
                {
                    Thread.Sleep(8);
                }
            }

            // Stopping the tap can leave one final IOProc buffer in the FIFO.
            for (var i = 0; i < 64; i++)
            {
                var n = BppMacAudio_Read(_handle, scratch, scratch.Length);
                if (n <= 0)
                    break;
                _wav!.WriteSamples(scratch, 0, n);
                AccumulateStats(scratch, n);
                Interlocked.Add(ref _totalFloats, n);
            }
        }
        catch (Exception ex)
        {
            _failureReason = ReplayAudioFailureReasonCode.CaptureLoopFailed;
            _failureException = ex;
        }
    }

    private void AccumulateStats(float[] buffer, int count)
    {
        double sumSquares = 0;
        var peak = _peakAbs;
        for (var i = 0; i < count; i++)
        {
            var sample = buffer[i];
            sumSquares += (double)sample * sample;
            var abs = sample < 0 ? -sample : sample;
            if (abs > peak)
                peak = abs;
        }

        _sumSquares += sumSquares;
        _statSampleCount += count;
        _peakAbs = peak;
    }

    [DllImport("BppMacAudio")]
    private static extern IntPtr BppMacAudio_Start(out int rate, out int ch);

    [DllImport("BppMacAudio")]
    private static extern int BppMacAudio_Read(IntPtr h, float[] dst, int max);

    [DllImport("BppMacAudio")]
    private static extern void BppMacAudio_Stop(IntPtr h);
}
