#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Derives the WAV path the loopback capture writes for a recording — a single, all-inclusive
/// device-output stem (music, settlement, and the spatialised combat/board SFX).
/// </summary>
internal static class ReplayVideoAudioTapPlan
{
    internal static string DeriveAudioWavPath(string tempVideoPath) =>
        DeriveAudioWavPath(tempVideoPath, "audio");

    private static string DeriveAudioWavPath(string tempVideoPath, string audioSuffix)
    {
        const string suffix = ".recording.mp4";
        if (
            string.IsNullOrEmpty(tempVideoPath)
            || !tempVideoPath.EndsWith(suffix, StringComparison.Ordinal)
        )
        {
            return tempVideoPath + "." + audioSuffix + ".wav";
        }

        return tempVideoPath.Substring(0, tempVideoPath.Length - suffix.Length)
            + "."
            + audioSuffix
            + ".wav";
    }
}
