#nullable enable

namespace BazaarPlusPlus.Game.PvpBattles;

public enum PvpSnapshotCombatant
{
    Player,
    Opponent,
}

public enum PvpSnapshotSection
{
    Hand,
    Skills,
}

public enum PvpSnapshotReasonCode
{
    LiveReadException,
    OpeningMessageException,
    OpeningDataException,
}
