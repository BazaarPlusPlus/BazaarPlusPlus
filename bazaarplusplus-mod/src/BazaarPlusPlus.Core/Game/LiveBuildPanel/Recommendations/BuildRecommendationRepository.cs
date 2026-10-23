#nullable enable
using System.Globalization;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Consumes the analyzer ten-win build catalog, answers recommendation queries against the live
/// state, and projects matched builds onto renderable item boards. The corpus is a static package —
/// recommendation queries read the catalog snapshot and never hit the server.
/// </summary>
public sealed class BuildRecommendationRepository
{
    private const int SocketCount = 10;

    private readonly IRemoteEmbeddedCatalog<TenWinBuildCorpus> _catalog;
    private readonly Func<Guid, CardSize?> _templateSize;

    /// <param name="catalog">The plugin-lifetime build catalog, owned by the composition root.</param>
    /// <param name="templateSize">
    /// The game's size for a template, used when a layout row carries no valid size; null when the
    /// host cannot answer. A card it cannot size renders small.
    /// </param>
    public BuildRecommendationRepository(
        IRemoteEmbeddedCatalog<TenWinBuildCorpus> catalog,
        Func<Guid, CardSize?>? templateSize = null
    )
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _templateSize = templateSize ?? (_ => null);
    }

    public IReadOnlyList<BuildRecommendation> FindRecommendations(
        string? hero,
        IReadOnlyCollection<Guid> selectedTemplateIds,
        BuildLiveState? liveState = null
    )
    {
        if (!LiveBuildPlayableHero.IsPlayable(hero))
            return Array.Empty<BuildRecommendation>();

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

        var results = new List<BuildRecommendation>(matches.Count);
        foreach (var match in matches)
        {
            var board = ProjectBoard(match.Build);
            if (board.Cards.Count == 0)
                continue;

            results.Add(
                new BuildRecommendation
                {
                    MatchedCardCount = match.MatchedSelectedCount,
                    TenWinRunCount = match.Build.Stats.TenWinRunCount,
                    TenWinRateBps = match.Build.Stats.TenWinRateBps,
                    P75TenWinFinalDay = match.Build.Stats.P75TenWinFinalDay,
                    Score = match.Build.Stats.Score,
                    Board = board,
                }
            );
        }

        return results;
    }

    private BppItemBoard ProjectBoard(TenWinBuild build)
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

    private BppItemBoardCard ProjectCard(TenWinLayoutItem item)
    {
        var size = ResolveCardSize(item.TemplateId, item.Size);
        return new BppItemBoardCard
        {
            TemplateId = item.TemplateId,
            InstanceId =
                $"tenwin-{(item.Slot?.ToString(CultureInfo.InvariantCulture) ?? "unsocketed")}-{item.TemplateId:N}",
            Order = item.Slot ?? 0,
            Tier = MapTier(item.Tier),
            Size = size,
            Span = BppItemBoardSpan.Resolve(size),
            SourceSocketId = item.Slot.HasValue
                ? Math.Clamp(item.Slot.Value, 0, SocketCount - 1)
                : null,
            EnchantmentType = MapEnchant(item.EnchantName),
        };
    }

    private CardSize ResolveCardSize(Guid templateId, int? size)
    {
        return size switch
        {
            1 => CardSize.Small,
            2 => CardSize.Medium,
            3 => CardSize.Large,
            // Out-of-range/absent payload size: fall back to the game's authoritative card size.
            _ => ResolveTemplateSize(templateId),
        };
    }

    private CardSize ResolveTemplateSize(Guid templateId)
    {
        try
        {
            return _templateSize(templateId) ?? CardSize.Small;
        }
        catch
        {
            return CardSize.Small;
        }
    }

    // Layout tier is the analyzer's value 1..5 (Bronze..Legendary); anything else clamps.
    private static CardTier MapTier(int? tier) =>
        tier.GetValueOrDefault() switch
        {
            <= 1 => CardTier.Bronze,
            2 => CardTier.Silver,
            3 => CardTier.Gold,
            4 => CardTier.Diamond,
            _ => CardTier.Legendary,
        };

    // The corpus names enchantments by member name. A numeric string never selects a facet: a
    // facet's integer belongs to BPP (ADR-0015).
    private static CardEnchantment? MapEnchant(string? enchantName)
    {
        var name = enchantName?.Trim();
        if (string.IsNullOrEmpty(name) || !char.IsLetter(name![0]))
            return null;

        return
            Enum.TryParse<CardEnchantment>(name, ignoreCase: true, out var enchantment)
            && Enum.IsDefined(typeof(CardEnchantment), enchantment)
            ? enchantment
            : null;
    }

    /// <summary>
    /// Snapshot of the currently loaded corpus's provenance (data window end, build/hero counts)
    /// for status surfaces; null while no corpus is loaded.
    /// </summary>
    public TenWinCorpusSummary? GetCorpusSummary()
    {
        var corpus = EnsureCorpus();
        return corpus == null
            ? (TenWinCorpusSummary?)null
            : new TenWinCorpusSummary(
                corpus.WindowEndUtc,
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

    public async Task<BuildRecommendationRefreshResult> TryRefreshFinalBuildsFromRemoteAsync()
    {
        var result = await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        if (result.Succeeded)
            return BuildRecommendationRefreshResult.Updated();

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
        return BuildRecommendationRefreshResult.Failure(
            reason,
            issue?.Detail ?? issue?.Exception?.Message,
            issue?.Exception
        );
    }
}
