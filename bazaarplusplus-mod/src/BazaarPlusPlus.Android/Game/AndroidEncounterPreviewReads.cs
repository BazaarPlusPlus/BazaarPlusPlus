using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.EncounterPreview;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Game;

/// <summary>
/// The Android host's reads for Core's <see cref="EncounterPreviewModule"/>: the plans
/// <see cref="AndroidEncounterPreview"/> read off static data, the run's hero, cards and day, and
/// each template's title and description as the game's own tooltip renders them. Main thread only.
/// </summary>
internal sealed class AndroidEncounterPreviewReads(AndroidEncounterPreview preview)
    : IEncounterPreviewReads
{
    public bool IsInCombat => Data.IsInCombat;

    public IEncounterPreviewPlans? TryGetPlans(out EncounterPreviewAvailability unavailable)
    {
        unavailable = EncounterPreviewAvailability.Loading;
        return preview.Plans is { } plans ? new Plans(plans.Snapshot) : null;
    }

    public CardHero? ReadCurrentHero() =>
        Data.Run?.Player is { } player
        && AndroidCardFacets.Hero.TryToFacet(player.Hero, out var hero)
        && hero != CardHero.Common
            ? hero
            : null;

    public EncounterInventory? ReadInventory()
    {
        var player = Data.Run?.Player;
        if (player == null)
            return null;
        var cards = new List<EncounterInventoryCard>();
        var seen = new HashSet<IntPtr>();
        void Add(Card card)
        {
            if (!seen.Add(card.Pointer) || !Guid.TryParse(card.TemplateId.ToString(), out var id))
                return;
            var template = card.Template;
            var item = card.TryCast<ItemCard>();
            var itemTemplate = template?.TryCast<TCardItem>();
            var active = item == null ? null : AndroidGameIds.Unboxed(() => item.Enchantment);
            var enchantmentTags = new HashSet<CardTag>();
            var enchantmentHiddenTags = new HashSet<CardHiddenTag>();
            if (
                active is { } type
                && itemTemplate != null
                && itemTemplate.TryGetEnchantmentTemplate(type, out var enchantment)
                && enchantment != null
            )
            {
                enchantmentTags = AndroidCardFacets.Tag.ToFacetSet(Materialize(enchantment.Tags));
                enchantmentHiddenTags = AndroidCardFacets.HiddenTag.ToFacetSet(
                    Materialize(enchantment.HiddenTags)
                );
            }
            var effectiveTags = new EffectiveCardTags(
                AndroidCardFacets.Tag.ToFacetSet(Materialize(card.Tags)),
                AndroidCardFacets.Tag.ToFacetSet(Materialize(template?.Tags)),
                enchantmentTags,
                AndroidCardFacets.HiddenTag.ToFacetSet(Materialize(card.HiddenTags)),
                AndroidCardFacets.HiddenTag.ToFacetSet(Materialize(template?.HiddenTags)),
                enchantmentHiddenTags
            );
            cards.Add(new EncounterInventoryCard(id, effectiveTags));
        }
        foreach (var card in Data.Entities.Values)
            if (
                card != null
                && card.Owner?.Pointer == player.Pointer
                && AndroidGameIds.Unboxed(() => card.Section)
                    is EInventorySection.Hand
                        or EInventorySection.Stash
            )
                Add(card);
        if (player.Skills != null)
            foreach (var skill in player.Skills)
                if (skill != null)
                    Add(skill);
        return new(cards);
    }

    private static HashSet<T> Materialize<T>(Il2CppSystem.Collections.Generic.HashSet<T>? source)
        where T : unmanaged
    {
        var values = new HashSet<T>();
        if (source != null)
            foreach (var value in source)
                values.Add(value);
        return values;
    }

    // The desktop colors keywords in the plain lines it builds. A description the game's tooltip
    // rendered already carries its colors and sprites, so it is left as it is.
    public string ColorKeywords(string text)
    {
        if (text.Contains('<'))
            return text;
        try
        {
            return Data.TooltipTypography?.ColorKeywords(
                    text,
                    false,
                    false,
                    new Il2CppSystem.Nullable<ECardAttributeType>()
                ) ?? text;
        }
        catch (Exception)
        {
            return text;
        }
    }

    // A static template with the title and description the current language and the game's
    // tooltip give it.
    public EncounterPreviewTemplatePlan Present(EncounterPreviewTemplatePlan template)
    {
        var id = template.TemplateId;
        var title = AndroidCardTemplateText.Title(id) ?? template.Title.FallbackText;
        var description =
            template.Kind == EncounterPreviewTemplateKind.CombatEncounter
                ? template.Description.FallbackText
                : AndroidCardTemplateText.Description(id) ?? template.Description.FallbackText;
        return new(
            id,
            template.Kind,
            template.Heroes,
            template.InternalName,
            new(null, title),
            new(null, description),
            null,
            template.RewardFilter
        );
    }

    // Static data's plans render no live value: the game's tooltip already filled in the
    // descriptions Present gives.
    private sealed class Plans(EncounterPreviewSnapshot snapshot) : IEncounterPreviewPlans
    {
        public EncounterPreviewSnapshot Snapshot => snapshot;

        public (int? Day, GameDataDayTierTable? Table) ReadDayTiers() =>
            (Data.Run is { } run ? (int)run.Day : null, AndroidDayTiers.Current());

        public LiveAbilityValueResolver? LiveValues => null;
    }
}
