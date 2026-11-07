using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.EventPreview;
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

    public IEncounterPreviewPlans? TryGetPlans(out EventPreviewAvailability unavailable)
    {
        unavailable = EventPreviewAvailability.Loading;
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
            var tags = new HashSet<string>(StringComparer.Ordinal);
            if (card.Tags != null)
                foreach (var tag in card.Tags)
                    tags.Add(tag.ToString());
            if (card.HiddenTags != null)
                foreach (var tag in card.HiddenTags)
                    tags.Add(tag.ToString());
            if (card.Template is { } template)
            {
                if (template.Tags != null)
                    foreach (var tag in template.Tags)
                        tags.Add(tag.ToString());
                if (template.HiddenTags != null)
                    foreach (var hiddenTag in template.HiddenTags)
                        tags.Add(hiddenTag.ToString());
                var item = card.TryCast<ItemCard>();
                var itemTemplate = template.TryCast<TCardItem>();
                var active = item == null ? null : AndroidGameIds.Unboxed(() => item.Enchantment);
                if (
                    active is { } enchantmentType
                    && itemTemplate != null
                    && itemTemplate.TryGetEnchantmentTemplate(enchantmentType, out var enchantment)
                    && enchantment != null
                )
                {
                    if (enchantment.Tags != null)
                        foreach (var tag in enchantment.Tags)
                            tags.Add(tag.ToString());
                    if (enchantment.HiddenTags != null)
                        foreach (var hiddenTag in enchantment.HiddenTags)
                            tags.Add(hiddenTag.ToString());
                }
            }
            cards.Add(new(id, tags));
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
