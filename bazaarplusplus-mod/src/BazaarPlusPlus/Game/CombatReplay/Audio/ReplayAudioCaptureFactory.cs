#nullable enable
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

// Resonance Audio's spatialized combat and board effects bypass tappable FMOD channel groups, so capture uses the rendered output.
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
            return false;
        }
    }

    [DllImport("BppMacAudio")]
    private static extern int BppMacAudio_IsSupported();
}

internal sealed class UnsupportedPlatformAudioCapture : IReplayAudioCaptureTap
{
    private readonly string _wavFilePath;

    public UnsupportedPlatformAudioCapture(string wavFilePath) => _wavFilePath = wavFilePath;

    public bool IsCapturing => false;
    public bool CapturedAnySamples => false;
    public long CapturedSampleFloats => 0;
    public string WavFilePath => _wavFilePath;
    public ReplayAudioBackend Backend => ReplayAudioBackend.Unsupported;
    public int SampleRateHz => 0;
    public int Channels => 0;
    public string SampleFormat => "none";
    public ReplayAudioFailureReasonCode FailureReason =>
        ReplayAudioFailureReasonCode.UnsupportedPlatform;
    public Exception? FailureException => null;
    public double RmsAmplitude => 0.0;
    public float PeakAmplitude => 0f;

    public ReplayAudioCaptureStartOutcome TryStart() =>
        ReplayAudioCaptureStartOutcome.Failure(ReplayAudioFailureReasonCode.UnsupportedPlatform);

    public void Stop() { }

    public void Dispose() { }
}
