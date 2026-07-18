#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Audio;

internal static class ReplayAudioCaptureThreadJoiner
{
    internal const int StopTimeoutMs = 3000;

    internal static bool TryJoin(
        Thread? thread,
        int timeoutMs,
        out TimeoutException? timeoutException
    )
    {
        timeoutException = null;
        if (thread == null || thread.Join(timeoutMs))
            return true;

        timeoutException = new TimeoutException(
            "Replay audio capture thread did not stop within the fixed timeout."
        );
        return false;
    }
}
