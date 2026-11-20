#nullable enable
using System.Globalization;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

// Query on the host's main thread. The projection cache is shared across queries and has no synchronization.
public sealed class BuildRecommendationRepository
{
    private const int SocketCount = 10;

    private readonly IRemoteEmbeddedCatalog<TenWinBuildCorpus> _catalog;
    private readonly Func<Guid, CardSize?> _templateSize;

    // Build IDs are per-hero row indices, so key projections by the build itself.
    private readonly Dictionary<TenWinBuild, BppItemBoard> _projectedBoards = new();
    private TenWinBuildCorpus? _projectedCorpus;

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
        BuildLiveState? liveState = null,
        bool includeTiny = true
    )
    {
        if (!LiveBuildPlayableHero.IsPlayable(hero))
            return Array.Empty<BuildRecommendation>();

        var corpus = EnsureCorpus();
        if (corpus == null)
            return Array.Empty<BuildRecommendation>();

        // Invalidate even on a query with no matches so old projected boards do not retain the replaced corpus.
        if (!ReferenceEquals(corpus, _projectedCorpus))
        {
            _projectedBoards.Clear();
            _projectedCorpus = corpus;
        }

        var matches = corpus.FindBuilds(
            hero,
            selectedTemplateIds ?? Array.Empty<Guid>(),
            liveState ?? BuildLiveState.Empty,
            includeTiny ? null : build => !ContainsTiny(build)
        );
        if (matches.Count == 0)
            return Array.Empty<BuildRecommendation>();

        var results = new List<BuildRecommendation>(matches.Count);
        foreach (var match in matches)
        {
            if (!_projectedBoards.TryGetValue(match.Build, out var board))
            {
                board = ProjectBoard(match.Build);
                _projectedBoards.Add(match.Build, board);
            }
            if (board.Cards.Count == 0)
                continue;

            results.Add(
                new BuildRecommendation
                {
                    MatchedCardCount = match.MatchedSelectedCount,
                    TenWinRunCount = match.Build.Stats.TenWinRunCount,
                    CompletedRunCount = match.Build.Stats.CompletedRunCount,
                    TenWinRateBps = match.Build.Stats.TenWinRateBps,
                    P75TenWinFinalDay = match.Build.Stats.P75TenWinFinalDay,
                    Score = match.Build.Stats.Score,
                    Board = board,
                }
            );
        }

        return results;
    }

    // A lookup failure cannot establish that the native board shrank a card.
    private bool ContainsTiny(TenWinBuild build) =>
        build.Layout.Any(item =>
            item.Size is { } size
            && size < BppItemBoardSpan.Resolve(ResolveTemplateSize(item.TemplateId))
        );

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
            // Parse rejects out-of-range sizes so the cache cannot freeze a Small fallback from unready game data.
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

    // Wire tiers use the analyzer's 1–5 numbering for Bronze through Legendary.
    private static CardTier MapTier(int? tier) =>
        tier.GetValueOrDefault() switch
        {
            <= 1 => CardTier.Bronze,
            2 => CardTier.Silver,
            3 => CardTier.Gold,
            4 => CardTier.Diamond,
            _ => CardTier.Legendary,
        };

    // The analyzer sends game member names. BPP facet integers are local and cannot identify a wire enchantment.
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
