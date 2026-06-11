#nullable enable

using System;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
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
        // Success prefix glyph for the corpus summary line.
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
        + FinalBuildRefreshFailed(Unknown());
}
