#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

public static partial class HistoryPanelText
{
    public static string BattleSnapshotMissing() =>
        FormatSimple("This battle has no saved board snapshot.", "这场战斗没有保存棋盘快照。");

    public static string PartialPreview(int count) =>
        FormatSimple(
            $"{count} cards are unavailable in this game version.",
            $"{count} 张卡牌在当前版本不可用。"
        );
}
