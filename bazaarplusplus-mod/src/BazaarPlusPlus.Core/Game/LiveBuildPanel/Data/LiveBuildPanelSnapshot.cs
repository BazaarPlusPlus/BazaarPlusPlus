#nullable enable

using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

public sealed class LiveBuildPanelSnapshot
{
    public CardHero? Hero { get; init; }

    public BppItemBoard FinalBuild { get; init; } = BppItemBoard.Empty;

    public BppItemBoard Shop { get; init; } = BppItemBoard.Empty;

    public BppItemBoard Board { get; init; } = BppItemBoard.Empty;

    public BppItemBoard Stash { get; init; } = BppItemBoard.Empty;

    public IReadOnlyCollection<Guid> CandidateTemplateIds { get; init; } = Array.Empty<Guid>();

    public bool IncludeTiny { get; init; } = true;

    public LiveBuildMatchesState MatchesState { get; init; }

    public string MatchesGuidance { get; init; } = string.Empty;

    public BuildRecommendation? Recommendation { get; init; }

    public IReadOnlyList<LiveBuildMetric> Metrics { get; init; } = Array.Empty<LiveBuildMetric>();

    public int RecommendationIndex { get; init; }

    public int RecommendationCount { get; init; }

    public string PagerText =>
        RecommendationCount > 0 ? $"{RecommendationIndex + 1} / {RecommendationCount}" : "0 / 0";

    public bool CanBrowse => RecommendationCount > 1;

    public string FinalBuildRefreshButtonText { get; init; } = string.Empty;

    public bool FinalBuildRefreshButtonEnabled { get; init; } = true;

    public string CorpusStatusText { get; init; } = string.Empty;

    public string CorpusStatusTooltip { get; init; } = string.Empty;

    public LiveBuildRefreshSeverity CorpusStatusSeverity { get; init; }

    public LiveBuildCorpusState CorpusState { get; init; }

    public string CorpusFreshnessText { get; init; } = string.Empty;

    public LiveBuildRefreshSeverity CorpusFreshnessSeverity { get; init; }

    public IReadOnlyList<string> CorpusDetailLines =>
        new[] { CorpusStatusText, CorpusFreshnessText, CorpusStatusTooltip }
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

    public IReadOnlyList<BPPSupporterSample> Supporters { get; init; } =
        Array.Empty<BPPSupporterSample>();

    public bool HasActiveRun => Hero != null;

    public LiveItemBoardRowVm[] Rows =>
        [
            new LiveItemBoardRowVm(FinalBuild, LiveBuildPanelText.FinalBuildRow(), string.Empty),
            new LiveItemBoardRowVm(
                Shop,
                LiveBuildPanelText.ShopRow(),
                HasActiveRun ? LiveBuildPanelText.EmptyShop() : string.Empty
            ),
            new LiveItemBoardRowVm(
                Stash,
                LiveBuildPanelText.StashRow(),
                HasActiveRun ? LiveBuildPanelText.EmptyStash() : string.Empty
            ),
            new LiveItemBoardRowVm(
                Board,
                LiveBuildPanelText.BoardRow(),
                HasActiveRun ? LiveBuildPanelText.EmptyBoard() : string.Empty
            ),
        ];
}
