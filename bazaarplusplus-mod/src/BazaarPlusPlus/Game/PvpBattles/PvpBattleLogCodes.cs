#nullable enable

namespace BazaarPlusPlus.Game.PvpBattles;

internal enum PvpSnapshotCombatant
{
    Player,
    Opponent,
}

internal enum PvpSnapshotSection
{
    Hand,
    Skills,
}

internal enum PvpSnapshotReasonCode
{
    LiveReadException,
    OpeningMessageException,
    OpeningDataException,
}
