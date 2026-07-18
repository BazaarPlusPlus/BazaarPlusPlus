#nullable enable

using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    public static string FontAtlasSample() =>
        Title()
        + Subtitle()
        + FinalBuildRow()
        + ShopRow()
        + BoardRow()
        + StashRow()
        + Close()
        + Previous()
        + Next()
        + NoRun()
        + NoCandidates()
        + NoRecommendation()
        + EmptyShop()
        + EmptyBoard()
        + EmptyStash()
        + L.Resolve(TenWinLabelText)
        + CorpusCardTitle()
        + ResultCardTitle()
        + RefreshFinalBuilds()
        + Working()
        + RefreshingFinalBuilds()
        + CorpusEmpty()
        // Candidate-marker glyph drawn over picked board cards.
        + "✓"
        // Fixed sample covering the corpus-summary labels/units plus every digit glyph.
        + CorpusSummaryTooltip(
            new TenWinCorpusSummary(
                new DateTimeOffset(2034, 5, 16, 7, 28, 9, TimeSpan.Zero),
                1234567890,
                1234567890,
                [
                    new TenWinHeroBuildCount("Vanessa", 1234567890),
                    new TenWinHeroBuildCount("Dooley", 987654321),
                ]
            )
        )
        // Hero short codes for the corpus dashboard tiles.
        + "VAN DOO PYG MAK JUL KAR STE UNK"
        // Relative-time freshness buckets (latin + CJK) and the thousands separator.
        + "updated m h d w ago — 1,234"
        + "更新于 更新於 分钟前 分鐘前 小时前 小時前 天前 周前 週前 更新时间未知 更新時間未知"
        // Matches card stat labels.
        + MatchRateLabel()
        + MatchSampleLabel()
        + MatchFinalDayLabel()
        + MatchMatchedLabel()
        + FinalBuildRefreshFailed(Unknown());
}
