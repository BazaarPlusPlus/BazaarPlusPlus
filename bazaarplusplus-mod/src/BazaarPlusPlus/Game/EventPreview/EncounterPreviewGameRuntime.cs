#nullable enable
using System.Collections;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarPlusPlus.Game.Encounters;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.EventPreview;

internal interface IEncounterPreviewGameRuntime
{
    bool IsInCombat { get; }
    object? TryGetReadyStaticData();
    BppGameDataSourceInfo? TryCaptureSourceInfo(object source);
    Task<Dictionary<Guid, ITCard>?> LoadCardMapAsync(object source);
    Dictionary<int, TLevelUp>? SnapshotLevelUps(object source);
    TCardBase? GetCardTemplate(object source, Guid templateId);
    EHero? ReadCurrentHero();
    EncounterInventory? ReadInventory();
    int? ReadCurrentDay();
    ETier? ReadDayTierCeiling(int? currentDay);
    TierDistribution? ReadDayTierDistribution(object source, int? currentDay);
    string ColorKeywords(string text);
}

internal sealed class EncounterPreviewGameRuntime(BppStaticCardMapProvider cardMapProvider)
    : IEncounterPreviewGameRuntime
{
    private readonly BppStaticCardMapProvider _cardMapProvider =
        cardMapProvider ?? throw new ArgumentNullException(nameof(cardMapProvider));

    public bool IsInCombat => Data.IsInCombat;

    public object? TryGetReadyStaticData() => BppStaticDataAccess.TryGetReadyManagerObject();

    public BppGameDataSourceInfo? TryCaptureSourceInfo(object source) =>
        BppStaticDataAccess.TryCaptureGameDataSourceInfo(source);

    public Task<Dictionary<Guid, ITCard>?> LoadCardMapAsync(object source) =>
        _cardMapProvider.BeginLoad(source);

    public Dictionary<int, TLevelUp>? SnapshotLevelUps(object source) =>
        BppStaticDataAccess.SnapshotLevelUps(source);

    public TCardBase? GetCardTemplate(object source, Guid templateId) =>
        BppStaticDataAccess.GetCardTemplate(source, templateId);

    public EHero? ReadCurrentHero()
    {
        var runHero = Data.Run?.Player?.Hero;
        if (IsConcreteHero(runHero))
            return runHero;
        var selectedHero = Data.SelectedHero;
        return IsConcreteHero(selectedHero) ? selectedHero : null;
    }

    public EncounterInventory? ReadInventory()
    {
        try
        {
            var player = Data.Run?.Player;
            if (player == null)
                return null;

            var cards = new List<EncounterInventoryCard>();
            AddCards(cards, player.Hand?.GetItemsAsEnumerable());
            AddCards(cards, player.Stash?.GetItemsAsEnumerable());
            AddCards(cards, player.Skills);
            return new EncounterInventory(cards);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                TooltipLogEvents.EncounterInventoryDegraded,
                ex,
                TooltipLogEvents.EncounterInventoryReasonCode.Bind(
                    TooltipLogReasonCode.InventoryReadException
                )
            );
            return null;
        }
    }

    public int? ReadCurrentDay()
    {
        try
        {
            return (int?)Data.Run?.Day;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public ETier? ReadDayTierCeiling(int? currentDay) =>
        currentDay.HasValue ? DayTierSchedule.CeilingTier(currentDay.Value) : null;

    public TierDistribution? ReadDayTierDistribution(object source, int? currentDay)
    {
        if (!currentDay.HasValue)
            return null;

        try
        {
            var run = Data.Run;
            if (run == null)
                return null;
            var weights = BppStaticDataAccess.GetItemSkillSpawnTierProbabilities(
                source,
                run.GameModeId,
                currentDay.Value
            );
            return weights == null
                ? null
                : TierDistribution.FromWeights(
                    weights.Bronze,
                    weights.Silver,
                    weights.Gold,
                    weights.Diamond
                );
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string ColorKeywords(string text) => BppTooltipText.ColorKeywords(text);

    private static bool IsConcreteHero(EHero? hero) => hero.HasValue && hero.Value != EHero.Common;

    private static void AddCards(List<EncounterInventoryCard> cards, IEnumerable? source)
    {
        if (source == null)
            return;
        foreach (var card in source)
        {
            if (card is not BazaarGameClient.Domain.Models.Cards.Card typed)
                continue;
            var tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in typed.Tags)
                tags.Add(tag.ToString());
            foreach (var hiddenTag in typed.HiddenTags)
                tags.Add(hiddenTag.ToString());
            cards.Add(new EncounterInventoryCard(typed.TemplateId, tags));
        }
    }
}
