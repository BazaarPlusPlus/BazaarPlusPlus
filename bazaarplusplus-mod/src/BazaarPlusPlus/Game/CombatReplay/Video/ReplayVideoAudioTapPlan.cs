#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

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
