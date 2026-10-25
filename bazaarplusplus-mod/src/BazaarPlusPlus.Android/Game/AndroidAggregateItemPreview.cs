using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Localization;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Effect;
using Il2CppBazaarGameShared.Domain.Effect.Actions;
using Il2CppBazaarGameShared.Domain.Effect.AuraActions;
using Il2CppBazaarGameShared.Domain.Targeting;
using Il2CppBazaarGameShared.Domain.Values.ReferenceValues;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Utilities;

namespace BazaarPlusPlus.Android.Game;

internal sealed class AndroidAggregateItemPreview(BppSettings settings)
{
    public string? Render(Card? card)
    {
        if (!settings.EnableDetailedPreviewConfig.Value || card == null || !Data.IsManagerCreated())
            return null;
        var template = Data.GetStatic()?.GetCardById(card.TemplateId)?.TryCast<TCardBase>();
        if (template == null)
            return null;
        var found = Source(
            template.Auras,
            template.Abilities,
            out var section,
            out var excludeSelf
        );
        var item = card.TryCast<ItemCard>();
        var enchantment = item == null ? null : AndroidGameIds.Unboxed(() => item.Enchantment);
        var itemTemplate = template.TryCast<TCardItem>();
        if (
            !found
            && enchantment is { } active
            && itemTemplate != null
            && itemTemplate.TryGetEnchantmentTemplate(active, out var enchanted)
            && enchanted != null
        )
            found = Source(enchanted.Auras, enchanted.Abilities, out section, out excludeSelf);
        if (!found)
            return null;
        var present = new HashSet<CardTag>();
        void Add(Card owned)
        {
            if (excludeSelf && owned.Pointer == card.Pointer)
                return;
            if (owned.Tags != null)
                foreach (var tag in owned.Tags)
                    if (AndroidCardFacets.Tag.TryToFacet(tag, out var facet))
                        present.Add(facet);
        }
        if (section == null)
            Add(card);
        else
        {
            var player = Data.Run?.Player;
            if (player == null)
                return null;
            if (
                section
                is ETargetCardSectionTargetSection.SelfHand
                    or ETargetCardSectionTargetSection.SelfHandAndStash
            )
                if (player.Hand?.GetItems() is { } hand)
                    foreach (var owned in hand)
                        if (owned?.TryCast<Card>() is { } live)
                            Add(live);
            if (
                section
                is ETargetCardSectionTargetSection.SelfStash
                    or ETargetCardSectionTargetSection.SelfHandAndStash
            )
                if (player.Stash?.GetItems() is { } stash)
                    foreach (var owned in stash)
                        if (owned?.TryCast<Card>() is { } live)
                            Add(live);
        }
        var missing = CardFacetOrder
            .PlayerFacingTags.Where(tag =>
                tag != CardTag.Merchant
                && AndroidCardFacets.Tag.TryToSource(tag, out _)
                && !present.Contains(tag)
            )
            .ToArray();
        return missing.Length == 0
            ? null
            : L.Resolve(new LocalizedTextSet("Missing Types:", "尚缺类型：", "尚缺類型："))
                + " "
                + string.Join(
                    ", ",
                    missing.Select(tag => new LocalizableText(tag.ToString()).GetLocalizedText())
                );
    }

    private static bool Source(
        Il2CppSystem.Collections.Generic.Dictionary<string, TCardAura> auras,
        Il2CppSystem.Collections.Generic.Dictionary<string, TCardAbility> abilities,
        out ETargetCardSectionTargetSection? section,
        out bool excludeSelf
    )
    {
        section = null;
        excludeSelf = false;
        if (auras != null)
            foreach (var aura in auras.Values)
                if (aura.Action?.TryCast<TAuraActionCardAddTagsBySource>() != null)
                    return true;
        if (abilities != null)
            foreach (var ability in abilities.Values)
                if (ability.Action?.TryCast<TActionCardAddTagsBySource>() != null)
                    return true;
        if (auras != null)
            foreach (var aura in auras.Values)
            {
                var value = aura
                    .Action?.TryCast<TAuraActionCardModifyAttribute>()
                    ?.Value?.TryCast<TReferenceValueCardTagCount>();
                var target = value?.Target?.TryCast<TTargetCardSection>();
                if (value?.Distinct != true || target == null)
                    continue;
                section = target.TargetSection;
                excludeSelf = target.ExcludeSelf;
                return true;
            }
        return false;
    }
}
