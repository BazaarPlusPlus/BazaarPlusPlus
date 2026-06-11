#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

/// <summary>
/// Captures the game's audio by recording the default render endpoint in WASAPI LOOPBACK mode — i.e.
/// the exact PCM the speakers play. This is independent of FMOD's internal bus/DSP routing, so it
/// captures everything the player hears: music, settlement, VO, AND the 3D combat/board SFX whose
/// spatialised signal does not appear on any tappable FMOD channel group (verified: no Resonance
/// Listener DSP exists on any Studio bus and the core master output is music-only).
///
/// A background thread pulls capture packets and writes them to a streaming float <see cref="WavStreamWriter"/>.
/// Everything degrades gracefully: any COM/WASAPI failure during <see cref="TryStart"/> tears the tap
/// down, logs a Warn, and returns false so the recorder proceeds with a silent video. Game playback is
/// never affected (loopback is read-only).
///
/// NOTE: default-endpoint loopback captures the whole render-endpoint mix, so any other app playing
/// sound to the same device is also recorded. For a typical recording the game is the only audio.
/// </summary>
internal sealed class WasapiLoopbackCaptureTap : IReplayAudioCaptureTap
{
    private const string Component = "CombatReplayAudio";

    private const int CLSCTX_ALL = 0x17;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const long RefTimesPerSecond = 10_000_000L; // 100ns units; 1s capture buffer

    private static readonly Guid CLSID_MMDeviceEnumerator = new(
        "BCDE0395-E52F-467C-8E3D-C4579291692E"
    );
    private static readonly Guid IID_IMMDeviceEnumerator = new(
        "A95664D2-9614-4F35-A746-DE8DB63617E6"
    );
    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new(
        "C8ADBD64-E71E-48a0-A4DE-185C395CD317"
    );

    private readonly string _wavFilePath;

    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;

    private WavStreamWriter? _wav;
    private Thread? _thread;
    private volatile bool _running;

    private int _sampleRate;
    private int _channels;
    private bool _isFloat;
    private int _bytesPerSample;

    private long _totalFloats;
    private double _sumSquares;
    private long _statSampleCount;
    private float _peakAbs;
    private bool _stopped;

    public WasapiLoopbackCaptureTap(string wavFilePath)
    {
        _wavFilePath = wavFilePath ?? throw new ArgumentNullException(nameof(wavFilePath));
    }

    public bool IsCapturing { get; private set; }
    public string WavFilePath => _wavFilePath;
    public string CapturePointLabel => "wasapi-loopback";
    public bool CapturedAnySamples => Interlocked.Read(ref _totalFloats) > 0;
    public long CapturedSampleFloats => Interlocked.Read(ref _totalFloats);
    public double RmsAmplitude =>
        _statSampleCount > 0 ? Math.Sqrt(_sumSquares / _statSampleCount) : 0.0;
    public float PeakAmplitude => _peakAbs;

    public bool TryStart()
    {
        IntPtr mixFormat = IntPtr.Zero;
        try
        {
            var clsid = CLSID_MMDeviceEnumerator;
            var iidEnum = IID_IMMDeviceEnumerator;
            var hr = CoCreateInstance(
                ref clsid,
                IntPtr.Zero,
                CLSCTX_ALL,
                ref iidEnum,
                out var enumObj
            );
            if (hr != 0 || enumObj == null)
            {
                BppLog.Warn(
                    Component,
                    $"Loopback: CoCreateInstance(MMDeviceEnumerator) hr=0x{hr:X}."
                );
                SafeStopInternal();
                return false;
            }

            _enumerator = (IMMDeviceEnumerator)enumObj;

            // eRender (0) / eConsole (0): the device the game plays to.
            hr = _enumerator.GetDefaultAudioEndpoint(0, 0, out _device);
            if (hr != 0 || _device == null)
            {
                BppLog.Warn(Component, $"Loopback: GetDefaultAudioEndpoint hr=0x{hr:X}.");
                SafeStopInternal();
                return false;
            }

            var iidClient = IID_IAudioClient;
            hr = _device.Activate(ref iidClient, CLSCTX_ALL, IntPtr.Zero, out var clientPtr);
            if (hr != 0 || clientPtr == IntPtr.Zero)
            {
                BppLog.Warn(Component, $"Loopback: Activate(IAudioClient) hr=0x{hr:X}.");
                SafeStopInternal();
                return false;
            }
            _audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(clientPtr);
            Marshal.Release(clientPtr);

            hr = _audioClient.GetMixFormat(out mixFormat);
            if (hr != 0 || mixFormat == IntPtr.Zero)
            {
                BppLog.Warn(Component, $"Loopback: GetMixFormat hr=0x{hr:X}.");
                SafeStopInternal();
                return false;
            }

            ParseWaveFormat(mixFormat);

            hr = _audioClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK,
                RefTimesPerSecond,
                0,
                mixFormat,
                IntPtr.Zero
            );
            if (hr != 0)
            {
                BppLog.Warn(Component, $"Loopback: IAudioClient.Initialize hr=0x{hr:X}.");
                SafeStopInternal();
                return false;
            }

            var iidCapture = IID_IAudioCaptureClient;
            hr = _audioClient.GetService(ref iidCapture, out var capturePtr);
            if (hr != 0 || capturePtr == IntPtr.Zero)
            {
                BppLog.Warn(Component, $"Loopback: GetService(IAudioCaptureClient) hr=0x{hr:X}.");
                SafeStopInternal();
                return false;
            }
            _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr);
            Marshal.Release(capturePtr);

            _wav = new WavStreamWriter(_wavFilePath, _sampleRate, _channels);

            hr = _audioClient.Start();
            if (hr != 0)
            {
                BppLog.Warn(Component, $"Loopback: IAudioClient.Start hr=0x{hr:X}.");
                SafeStopInternal();
                return false;
            }

            _running = true;
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "BPP.CombatReplayAudio.WasapiLoopback",
            };
            _thread.Start();

            IsCapturing = true;
            BppLog.Info(
                Component,
                $"Loopback capture started rate={_sampleRate} channels={_channels} format={(_isFloat ? "float" : _bytesPerSample * 8 + "bit-pcm")}"
            );
            return true;
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Loopback capture start failed: {ex.Message}");
            SafeStopInternal();
            return false;
        }
        finally
        {
            if (mixFormat != IntPtr.Zero)
                Marshal.FreeCoTaskMem(mixFormat);
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
            _thread?.Join(3000);
        }
        catch
        {
            // A failed join must not block teardown.
        }

        try
        {
            _audioClient?.Stop();
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Loopback: IAudioClient.Stop failed: {ex.Message}");
        }

        ReleaseCom(ref _captureClient);
        ReleaseCom(ref _audioClient);
        ReleaseCom(ref _device);
        ReleaseCom(ref _enumerator);

        try
        {
            _wav?.Dispose();
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Loopback: WAV close failed: {ex.Message}");
        }

        IsCapturing = false;
    }

    public void Dispose() => Stop();

    private void SafeStopInternal()
    {
        try
        {
            Stop();
        }
        catch
        {
            // Stop is fully guarded; final safety net so a start failure never escapes.
        }
    }

    private void CaptureLoop()
    {
        var capture = _captureClient;
        var wav = _wav;
        if (capture == null || wav == null)
            return;

        var scratch = new float[16384];
        try
        {
            while (_running)
            {
                if (capture.GetNextPacketSize(out var packetFrames) != 0)
                    break;

                while (packetFrames > 0)
                {
                    if (
                        capture.GetBuffer(
                            out var dataPtr,
                            out var numFrames,
                            out var flags,
                            out _,
                            out _
                        ) != 0
                    )
                    {
                        break;
                    }

                    var totalFloats = (int)numFrames * _channels;
                    if (totalFloats > 0)
                    {
                        if (totalFloats > scratch.Length)
                            scratch = new float[totalFloats];

                        if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || dataPtr == IntPtr.Zero)
                            Array.Clear(scratch, 0, totalFloats);
                        else
                            ConvertToFloat(dataPtr, scratch, totalFloats);

                        wav.WriteSamples(scratch, 0, totalFloats);
                        AccumulateStats(scratch, totalFloats);
                        Interlocked.Add(ref _totalFloats, totalFloats);
                    }

                    capture.ReleaseBuffer(numFrames);

                    if (capture.GetNextPacketSize(out packetFrames) != 0)
                        break;
                }

                Thread.Sleep(8);
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Loopback capture loop error: {ex.Message}");
        }
    }

    private void ConvertToFloat(IntPtr src, float[] dst, int count)
    {
        if (_isFloat && _bytesPerSample == 4)
        {
            Marshal.Copy(src, dst, 0, count);
            return;
        }

        if (!_isFloat && _bytesPerSample == 2)
        {
            unsafe
            {
                var s = (short*)src;
                for (var i = 0; i < count; i++)
                    dst[i] = s[i] / 32768f;
            }
            return;
        }

        if (!_isFloat && _bytesPerSample == 4)
        {
            unsafe
            {
                var s = (int*)src;
                for (var i = 0; i < count; i++)
                    dst[i] = (float)(s[i] / 2147483648.0);
            }
            return;
        }

        // Unknown format width: emit silence rather than garbage.
        Array.Clear(dst, 0, count);
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

    private void ParseWaveFormat(IntPtr pFormat)
    {
        // WAVEFORMATEX: wFormatTag(0,2) nChannels(2,2) nSamplesPerSec(4,4) ... wBitsPerSample(14,2) cbSize(16,2)
        var formatTag = (ushort)Marshal.ReadInt16(pFormat, 0);
        _channels = Marshal.ReadInt16(pFormat, 2);
        _sampleRate = Marshal.ReadInt32(pFormat, 4);
        var bits = (ushort)Marshal.ReadInt16(pFormat, 14);
        _bytesPerSample = Math.Max(1, bits / 8);

        const ushort WAVE_FORMAT_PCM = 1;
        const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
        const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

        if (formatTag == WAVE_FORMAT_IEEE_FLOAT)
            _isFloat = true;
        else if (formatTag == WAVE_FORMAT_PCM)
            _isFloat = false;
        else if (formatTag == WAVE_FORMAT_EXTENSIBLE)
        {
            // WAVEFORMATEXTENSIBLE.SubFormat GUID starts at offset 24; its Data1 is 1 (PCM) or 3 (float).
            var subFormatData1 = Marshal.ReadInt32(pFormat, 24);
            _isFloat = subFormatData1 == WAVE_FORMAT_IEEE_FLOAT;
        }
        else
            _isFloat = bits == 32; // best-effort guess

        if (_channels <= 0)
            _channels = 2;
        if (_sampleRate <= 0)
            _sampleRate = 48000;
    }

    private static void ReleaseCom<T>(ref T? obj)
        where T : class
    {
        if (obj == null)
            return;
        try
        {
            if (Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }
        catch
        {
            // Best-effort release.
        }
        obj = null;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr outer,
        int clsContext,
        ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object obj
    );

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsContext, IntPtr activationParams, out IntPtr iface);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(
            int shareMode,
            int streamFlags,
            long bufferDuration,
            long periodicity,
            IntPtr format,
            IntPtr sessionGuid
        );

        [PreserveSig]
        int GetBufferSize(out uint numFrames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out uint padding);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

        [PreserveSig]
        int GetMixFormat(out IntPtr format);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr handle);

        [PreserveSig]
        int GetService(ref Guid iid, out IntPtr iface);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(
            out IntPtr data,
            out uint numFramesToRead,
            out uint flags,
            out long devicePosition,
            out long qpcPosition
        );

        [PreserveSig]
        int ReleaseBuffer(uint numFramesRead);

        [PreserveSig]
        int GetNextPacketSize(out uint numFramesInNextPacket);
    }
}
