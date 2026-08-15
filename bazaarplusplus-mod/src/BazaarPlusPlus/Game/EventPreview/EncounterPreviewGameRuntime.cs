#nullable enable
using System.Collections;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.DayTiers;
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
    bool TryEvaluateAbilityValue(
        object source,
        Guid templateId,
        string effectId,
        bool isAura,
        out string valueText,
        out string? unit
    );
    EHero? ReadCurrentHero();
    EncounterInventory? ReadInventory();
    GameDataDayTierResolution ResolveDayTiers(object source);
    string ColorKeywords(string text);
}

internal sealed class EncounterPreviewGameRuntime(
    BppStaticCardMapProvider cardMapProvider,
    IGameDataDayTierResolver dayTierResolver
) : IEncounterPreviewGameRuntime
{
    private readonly BppStaticCardMapProvider _cardMapProvider =
        cardMapProvider ?? throw new ArgumentNullException(nameof(cardMapProvider));
    private readonly IGameDataDayTierResolver _dayTierResolver =
        dayTierResolver ?? throw new ArgumentNullException(nameof(dayTierResolver));

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

    public bool TryEvaluateAbilityValue(
        object source,
        Guid templateId,
        string effectId,
        bool isAura,
        out string valueText,
        out string? unit
    )
    {
        valueText = string.Empty;
        unit = null;
        var template = GetCardTemplate(source, templateId);
        var run = Data.Run;
        if (template == null || run == null)
            return false;

        try
        {
            var context = new ValueContext(run);
            CardAbilityValue value;
            var resolved = isAura
                ? CardAbilityValueReader.TryEvaluateAura(template, effectId, context, out value)
                : CardAbilityValueReader.TryEvaluate(template, effectId, context, out value);
            if (!resolved)
                return false;
            valueText = value.ValueText;
            unit = value.Unit;
            return true;
        }
        catch (Exception)
        {
            // Unsupported live targets (for example, Self without a targeting card)
            // degrade only this placeholder; the rest of the preview remains useful.
            return false;
        }
    }

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

    public GameDataDayTierResolution ResolveDayTiers(object source) =>
        _dayTierResolver.Resolve(source);

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
