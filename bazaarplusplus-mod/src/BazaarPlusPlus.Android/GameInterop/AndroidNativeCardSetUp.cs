using BazaarPlusPlus.Core.Cards.CardRental;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Interfaces;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Cards.Skill;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.UI;

namespace BazaarPlusPlus.Android.GameInterop;

// CardPreviewBase.SetUp replaces the client card's attributes, so reapply saved values afterward.
internal static class AndroidNativeCardSetUp
{
    private const string TinyArtKeyword = "_ISTINY_ON";

    public static Dictionary<ECardAttributeType, int> Values(
        IReadOnlyDictionary<string, int>? saved
    )
    {
        var values = new Dictionary<ECardAttributeType, int>();
        if (saved == null)
            return values;
        foreach (var pair in saved)
        {
            if (Enum.TryParse<ECardAttributeType>(pair.Key, false, out var attribute))
                values[attribute] = pair.Value;
        }
        return values;
    }

    public static TCardInstance Instance(
        TCardBase template,
        CardRentalSubject subject,
        string instanceId
    )
    {
        var tier = AndroidCardFacets.Tier.TryToSource(subject.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        var attributes = new Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int>();
        foreach (var pair in Values(subject.Attributes))
            attributes[pair.Key] = pair.Value;
        if (
            TinySize(template, subject) != null
            && Enum.TryParse<ECardAttributeType>("Tiny", false, out var tiny)
        )
            attributes[tiny] = 1;
        if (template.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = template.Id,
                TemplateVersion = string.Empty,
                InstanceId = instanceId,
                Tier = tier,
                Attributes = attributes,
            };
        }
        var item = new TCardInstanceItem
        {
            TemplateId = template.Id,
            TemplateVersion = string.Empty,
            InstanceId = instanceId,
            Tier = tier,
            Attributes = attributes,
        };
        if (
            subject.Enchantment is { } enchantment
            && AndroidCardFacets.Enchantment.TryToSource(enchantment, out var nativeEnchantment)
        )
            item.EnchantmentType = new Il2CppSystem.Nullable<EEnchantmentType>(nativeEnchantment);
        return item;
    }

    public static ECardSize ShownSize(TCardBase template, CardRentalSubject subject) =>
        template.Type == ECardType.Item
        && AndroidCardFacets.Size.TryToFacet(template.Size, out var templateSize)
        && AndroidCardFacets.Size.TryToSource(subject.ShownSize(templateSize), out var shown)
            ? shown
            : template.Size;

    // Look up Tiny by name because older Interop Snapshots can lack that enum member.
    public static ECardSize? TinySize(TCardBase template, CardRentalSubject subject) =>
        template.Type == ECardType.Item
        && AndroidCardFacets.Size.TryToFacet(template.Size, out var templateSize)
        && subject.IsTinyOn(templateSize)
            ? ShownSize(template, subject)
            : null;

    // Native setup uses the template size and clears the Tiny keyword on the fresh material.
    public static void ShowTiny(CardPreviewBase card, ECardSize size)
    {
        if (card._clientCard != null)
            card._clientCard.Size = size;
        if (card._cardMaterial != null)
            card._cardMaterial.EnableKeyword(TinyArtKeyword);
    }

    public static Kept Keep(TCardInstance instance) => new(instance.Attributes);

    public static void Fill(
        Card card,
        TCardBase template,
        ITCard cardTemplate,
        CardRentalSubject subject
    )
    {
        var tier = AndroidCardFacets.Tier.TryToSource(subject.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        card.Attributes = new Il2CppSystem.Collections.Generic.Dictionary<
            ECardAttributeType,
            int
        >();
        var tierData = template.TryCast<IHasTierData>();
        if (tierData?.Tiers != null)
            foreach (var entry in tierData.Tiers)
            {
                if (entry.Key > template.StartingTier)
                    continue;
                if (entry.Value?.Attributes == null)
                    continue;
                foreach (var attribute in entry.Value.Attributes)
                    card.Attributes[attribute.Key] = attribute.Value;
            }
        card.Tier = tier;
        var attributes = Il2CppTheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            cardTemplate,
            tier
        );
        var item = card.TryCast<ItemCard>();
        var itemTemplate = template.TryCast<TCardItem>();
        if (item != null && itemTemplate != null)
            Il2CppTheBazaar.CardExtensions.ApplyPreviewEnchantment(
                itemTemplate,
                item,
                attributes,
                subject.Enchantment is { } enchantment
                && AndroidCardFacets.Enchantment.TryToSource(enchantment, out var native)
                    ? new Il2CppSystem.Nullable<EEnchantmentType>(native)
                    : new Il2CppSystem.Nullable<EEnchantmentType>()
            );
        foreach (var pair in Values(subject.Attributes))
            attributes[pair.Key] = pair.Value;
        if (
            TinySize(template, subject) != null
            && Enum.TryParse<ECardAttributeType>("Tiny", false, out var tiny)
        )
            attributes[tiny] = 1;
        card.Attributes = attributes;
        card.Size = ShownSize(template, subject);
        card.Tags = template.Tags;
    }

    internal sealed class Kept
    {
        private readonly Dictionary<ECardAttributeType, int> _values = new();

        internal Kept(
            Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int>? instance
        )
        {
            if (instance == null)
                return;
            foreach (var pair in instance)
                _values[pair.Key] = pair.Value;
        }

        public bool WriteOver(
            Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int>? target
        )
        {
            if (_values.Count == 0 || target == null)
                return false;
            foreach (var pair in _values)
                target[pair.Key] = pair.Value;
            return true;
        }

        public void ShowOn(CardPreviewBase card)
        {
            var client = card._clientCard;
            if (client != null && WriteOver(client.Attributes))
                card.TryCast<CardPreviewItem>()
                    ?._gemGroupController?.Initialize(client, null, false);
        }
    }
}
