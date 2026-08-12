#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Consumes the analyzer-v4 ten-win build catalog, answers recommendation queries against the live
/// state, and projects matched builds onto renderable item boards. The corpus is a static package —
/// recommendation queries read the catalog snapshot and never hit the server.
/// </summary>
internal sealed class BuildRecommendationRepository
{
    private static readonly LocalizedTextSet FinalBuildLabel = new(
        "Ten-Win Build",
        "十胜阵容",
        "十勝陣容"
    );
    private readonly IRemoteEmbeddedCatalog<TenWinBuildCorpus> _catalog;

    internal BuildRecommendationRepository(IRemoteEmbeddedCatalog<TenWinBuildCorpus> catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public IReadOnlyList<BuildRecommendation> FindRecommendations(
        string? hero,
        IReadOnlyCollection<Guid> selectedTemplateIds,
        BuildLiveState? liveState = null
    )
    {
        var corpus = EnsureCorpus();
        if (corpus == null)
            return Array.Empty<BuildRecommendation>();

        var matches = corpus.FindBuilds(
            hero,
            selectedTemplateIds ?? Array.Empty<Guid>(),
            liveState ?? BuildLiveState.Empty
        );
        if (matches.Count == 0)
            return Array.Empty<BuildRecommendation>();

        var label = ResolveFinalBuildLabel();
        var results = new List<BuildRecommendation>(matches.Count);
        foreach (var match in matches)
        {
            var board = ProjectBoard(match.Build);
            if (board.Cards.Count == 0)
                continue;

            results.Add(
                new BuildRecommendation
                {
                    ModeLabel = label,
                    MatchedCardCount = match.MatchedSelectedCount,
                    TenWinRunCount = match.Build.Stats.TenWinRunCount,
                    TenWinRateBps = match.Build.Stats.TenWinRateBps,
                    P75TenWinFinalDay = match.Build.Stats.P75TenWinFinalDay,
                    Score = match.Build.Stats.Score,
                    Board = board,
                }
            );
        }

        for (var i = 0; i < results.Count; i++)
        {
            results[i].ResultIndex = i;
            results[i].ResultCount = results.Count;
        }

        return results;
    }

    private static BppItemBoard ProjectBoard(TenWinBuild build)
    {
        var cards = build
            .Layout.Where(item => item.TemplateId != Guid.Empty)
            .OrderBy(item => item.Slot ?? int.MaxValue)
            .Select(ProjectCard)
            .ToArray();

        return BppItemBoardSlotPlanner.Plan(
            new BppItemBoard(
                BppItemBoardId.FinalBuild,
                BppItemBoardType.Reference,
                cards,
                $"tenwin-build:{build.BuildId}"
            )
        );
    }

    private static BppItemBoardCard ProjectCard(TenWinLayoutItem item)
    {
        var size = ResolveCardSize(item.TemplateId, item.Size);
        return new BppItemBoardCard
        {
            TemplateId = item.TemplateId,
            InstanceId = $"tenwin-{(item.Slot?.ToString() ?? "unsocketed")}-{item.TemplateId:N}",
            Order = item.Slot ?? 0,
            Tier = MapTier(item.Tier),
            Size = size,
            Span = BppItemBoardSpan.Resolve(size),
            SourceSocketId = item.Slot.HasValue
                ? (EContainerSocketId?)Math.Clamp(item.Slot.Value, 0, 9)
                : null,
            EnchantmentType = MapEnchant(item.EnchantName),
        };
    }

    private static ECardSize ResolveCardSize(Guid templateId, int? size)
    {
        return size switch
        {
            1 => ECardSize.Small,
            2 => ECardSize.Medium,
            3 => ECardSize.Large,
            // Out-of-range/absent payload size: fall back to the game's authoritative card size.
            _ => ResolveCardSizeFromStaticData(templateId),
        };
    }

    private static ECardSize ResolveCardSizeFromStaticData(Guid templateId)
    {
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, templateId);
            return template?.Size switch
            {
                ECardSize.Small => ECardSize.Small,
                ECardSize.Medium => ECardSize.Medium,
                ECardSize.Large => ECardSize.Large,
                _ => ECardSize.Small,
            };
        }
        catch
        {
            return ECardSize.Small;
        }
    }

    private static ETier MapTier(int? tier)
    {
        // Layout tier is the analyzer's mod value 1..5 (Bronze..Legendary); ETier is 0..4.
        var normalized = tier.GetValueOrDefault();
        if (normalized > 0)
            normalized--;

        normalized = Math.Clamp(normalized, (int)ETier.Bronze, (int)ETier.Legendary);
        return (ETier)normalized;
    }

    private static EEnchantmentType? MapEnchant(string? enchantName)
    {
        if (string.IsNullOrWhiteSpace(enchantName))
            return null;

        return Enum.TryParse<EEnchantmentType>(enchantName, true, out var type)
            ? type
            : (EEnchantmentType?)null;
    }

    private static string ResolveFinalBuildLabel() => L.Resolve(FinalBuildLabel);

    /// <summary>
    /// Snapshot of the currently loaded corpus's provenance (analyzer emission time, build/hero
    /// counts) for status surfaces; null while no corpus is loaded.
    /// </summary>
    public TenWinCorpusSummary? GetCorpusSummary()
    {
        var corpus = EnsureCorpus();
        return corpus == null
            ? (TenWinCorpusSummary?)null
            : new TenWinCorpusSummary(
                corpus.GeneratedAtUtc,
                corpus.BuildCount,
                corpus.HeroCount,
                corpus.HeroBuildCounts
            );
    }

    private TenWinBuildCorpus? EnsureCorpus()
    {
        if (_catalog.TryGet(out var snapshot))
            return snapshot.Value;

        BeginCorpusLoad();
        return null;
    }

    public void BeginCorpusLoad()
    {
        _ = _catalog.WarmAsync(CancellationToken.None).AsTask();
    }

    internal async Task<BuildRecommendationRemoteRefreshResult> TryRefreshFinalBuildsFromRemoteAsync()
    {
        var result = await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        if (result.Succeeded)
            return BuildRecommendationRemoteRefreshResult.Success();

        var issue = result.Issue;
        var reason = issue?.Kind switch
        {
            CatalogIssueKind.RemoteEmpty => LiveBuildRefreshFailureReasonCode.RemoteEmptyResponse,
            CatalogIssueKind.RemoteInvalid =>
                LiveBuildRefreshFailureReasonCode.RemoteInvalidResponse,
            CatalogIssueKind.RemoteDownloadFailed =>
                LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
            _ => LiveBuildRefreshFailureReasonCode.RefreshException,
        };
        return BuildRecommendationRemoteRefreshResult.Failure(
            reason,
            issue?.Detail ?? issue?.Exception?.Message,
            issue?.Exception
        );
    }
}
