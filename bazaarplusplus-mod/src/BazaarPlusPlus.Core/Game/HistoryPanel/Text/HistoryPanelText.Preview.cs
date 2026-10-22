#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

public static partial class HistoryPanelText
{
    public static string PartialPreview(int count) =>
        FormatSimple(
            $"{count} cards are unavailable in this game version.",
            $"{count} 张卡牌在当前版本不可用。"
        );
}
