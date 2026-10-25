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

/// <summary>Whose field a <c>pvp_battles.participant.unreadable</c> read failed on.</summary>
public enum PvpBattleReadSide
{
    Battle,
    Player,
    Opponent,
}
