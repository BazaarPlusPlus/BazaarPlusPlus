#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// How every host labels one board capture of a PvP battle. A set is read when the opening game
/// sim arrives; a player set whose opening read failed or came back empty is read again from the
/// live game state, when the combat sim arrives and when the battle closes, until one live read
/// succeeds. An opening read that succeeded labels the set even after a live retry, as the desktop
/// has always stored it.
/// </summary>
public static class PvpBattleCaptureRule
{
    public static bool ShouldRetryLive(
        bool capturedFromOpening,
        bool capturedLive,
        int itemCount
    ) => !capturedLive && (!capturedFromOpening || itemCount == 0);

    public static (PvpBattleCaptureStatus Status, PvpBattleCaptureSource Source) Label(
        bool capturedFromOpening,
        bool capturedLive,
        int itemCount
    )
    {
        if (!capturedFromOpening && !capturedLive)
            return (PvpBattleCaptureStatus.Missing, PvpBattleCaptureSource.Unknown);
        var status =
            itemCount == 0 ? PvpBattleCaptureStatus.CapturedEmpty : PvpBattleCaptureStatus.Captured;
        return (
            status,
            capturedFromOpening
                ? PvpBattleCaptureSource.OpeningMessage
                : PvpBattleCaptureSource.LiveRetry
        );
    }
}
