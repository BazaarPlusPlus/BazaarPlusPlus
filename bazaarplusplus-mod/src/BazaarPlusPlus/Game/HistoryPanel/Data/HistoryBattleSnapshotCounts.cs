#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

internal readonly struct HistoryBattleSnapshotCounts
{
    public HistoryBattleSnapshotCounts(
        int playerHandItemCount,
        int playerSkillCount,
        int opponentHandItemCount,
        int opponentSkillCount
    )
        : this(
            playerHandItemCount,
            playerSkillCount,
            opponentHandItemCount,
            opponentSkillCount,
            known: true
        ) { }

    public HistoryBattleSnapshotCounts(
        int playerHandItemCount,
        int playerSkillCount,
        int opponentHandItemCount,
        int opponentSkillCount,
        bool known
    )
    {
        PlayerHandItemCount = playerHandItemCount;
        PlayerSkillCount = playerSkillCount;
        OpponentHandItemCount = opponentHandItemCount;
        OpponentSkillCount = opponentSkillCount;
        Known = known;
    }

    public int PlayerHandItemCount { get; }

    public int PlayerSkillCount { get; }

    public int OpponentHandItemCount { get; }

    public int OpponentSkillCount { get; }

    public bool Known { get; }

    public bool HasAnyRecordedCard =>
        PlayerHandItemCount > 0
        || PlayerSkillCount > 0
        || OpponentHandItemCount > 0
        || OpponentSkillCount > 0;

    public static HistoryBattleSnapshotCounts Empty => default;

    public static HistoryBattleSnapshotCounts Unknown => new(0, 0, 0, 0, known: false);
}
