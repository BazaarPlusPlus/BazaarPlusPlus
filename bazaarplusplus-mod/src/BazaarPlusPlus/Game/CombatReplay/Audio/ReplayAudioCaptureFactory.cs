#nullable enable
using System;
using System.Runtime.InteropServices;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

/// <summary>
/// Selects the platform-appropriate replay-audio capture backend.
///
/// Capture records the system audio OUTPUT (loopback) — exactly what the player hears — because the
/// game spatialises its 3D combat/board SFX with Google Resonance Audio, whose decoded signal never
/// appears on any tappable FMOD channel group; only device-output capture records it.
///
/// <list type="bullet">
/// <item>Windows: <see cref="WasapiLoopbackCaptureTap"/> (WASAPI loopback).</item>
/// <item>macOS (>= 15): <see cref="CoreAudioProcessTapCaptureTap"/> (CoreAudio process tap of the game's
/// own output PCM, via the native <c>BppMacAudio</c> dylib). The version gate is decided inside the
/// dylib via <c>NSProcessInfo</c>; a missing dylib degrades to a silent video.</item>
/// <item>pre-15 macOS / other platforms: not supported — returns a no-op so the recorder produces a
/// silent video.</item>
/// </list>
/// </summary>
internal static class ReplayAudioCaptureFactory
{
    public static IReplayAudioCaptureTap Create(string wavFilePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WasapiLoopbackCaptureTap(wavFilePath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && IsSupported())
            return new CoreAudioProcessTapCaptureTap(wavFilePath);

        return new UnsupportedPlatformAudioCapture(wavFilePath);
    }

    private static bool IsSupported()
    {
        try
        {
            // >= 15 is decided inside the dylib via NSProcessInfo.
            return BppMacAudio_IsSupported() != 0;
        }
        catch (DllNotFoundException)
        {
            // A machine too old to even load the dylib degrades to a silent video instead of throwing.
            return false;
        }
    }

    [DllImport("BppMacAudio")]
    private static extern int BppMacAudio_IsSupported();
}

/// <summary>
/// No-op capture for platforms without a supported loopback backend (pre-15 macOS / other non-Windows platforms). Its
/// <see cref="TryStart"/> returns false, so the recorder proceeds with a silent video and nothing is
/// captured. Replace by returning a real backend from <see cref="ReplayAudioCaptureFactory"/>.
/// </summary>
internal sealed class UnsupportedPlatformAudioCapture : IReplayAudioCaptureTap
{
    private readonly string _wavFilePath;

    public UnsupportedPlatformAudioCapture(string wavFilePath) => _wavFilePath = wavFilePath;

    public bool IsCapturing => false;
    public bool CapturedAnySamples => false;
    public long CapturedSampleFloats => 0;
    public string WavFilePath => _wavFilePath;
    public string CapturePointLabel => "unsupported-platform";
    public double RmsAmplitude => 0.0;
    public float PeakAmplitude => 0f;

    public bool TryStart()
    {
        BppLog.Warn(
            "CombatReplayAudio",
            $"Replay audio capture is not implemented on '{RuntimeInformation.OSDescription}'; recording a silent video."
        );
        return false;
    }

    public void Stop() { }

    public void Dispose() { }
}
