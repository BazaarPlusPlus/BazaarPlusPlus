#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

internal readonly struct ReplayAudioCaptureResult
{
    public string WavPath { get; init; }

    public bool CapturedAnySamples { get; init; }

    public long CapturedSampleFloats { get; init; }

    public long FileSizeBytes { get; init; }

    public string CapturePointLabel { get; init; }

    public double RmsAmplitude { get; init; }

    public float PeakAmplitude { get; init; }

    public bool Usable { get; init; }

    public ReplayAudioBackend Backend { get; init; }

    public int SampleRateHz { get; init; }

    public int Channels { get; init; }

    public string SampleFormat { get; init; }

    public ReplayAudioFailureReasonCode FailureReason { get; init; }

    public System.Exception? FailureException { get; init; }
}
