using BazaarPlusPlus.Core.Cards.CardRental;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Interfaces;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Cards.Skill;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// A card BPP sets up from a template, the rented native face or the detached text card, shows
/// the Saved Card Values its instance or subject carries, written over the template's tier and
/// enchantment values, as on the desktop (<c>NativeCardSetUp</c>). The game's
/// <c>CardPreviewBase.SetUp</c> gives the client card a new dictionary with the tier's and the
/// enchantment's values, so the saved values go on after it: a caller takes <see cref="Keep"/>
/// before it runs the set-up and calls <see cref="Kept.ShowOn"/> after a successful one.
/// Main thread only, except <see cref="Values"/>, which names no IL2CPP object.
/// </summary>
internal static class AndroidNativeCardSetUp
{
    private const string TinyArtKeyword = "_ISTINY_ON";

    /// <summary>
    /// Saved Card Values, keyed by the game's member name, as the game's attributes; a name the
    /// game does not know is dropped (ADR-0017). Empty when there are none.
    /// </summary>
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

    /// <summary>
    /// The synthetic instance a subject sets up: its tier (the template's starting tier when the
    /// facet has no game value), its enchantment on an item, and its saved values.
    /// </summary>
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

    /// <summary>
    /// The size an item shows at: the subject's saved size, else its template's. A skill and a
    /// size with no game value show the template's.
    /// </summary>
    public static ECardSize ShownSize(TCardBase template, CardRentalSubject subject) =>
        template.Type == ECardType.Item
        && AndroidCardFacets.Size.TryToFacet(template.Size, out var templateSize)
        && AndroidCardFacets.Size.TryToSource(subject.ShownSize(templateSize), out var shown)
            ? shown
            : template.Size;

    /// <summary>
    /// The size of an item saved below its template's size, else null. Its <c>Tiny</c> attribute
    /// goes by name: the Interop Snapshot this host compiles against can predate the member.
    /// </summary>
    public static ECardSize? TinySize(TCardBase template, CardRentalSubject subject) =>
        template.Type == ECardType.Item
        && AndroidCardFacets.Size.TryToFacet(template.Size, out var templateSize)
        && subject.IsTinyOn(templateSize)
            ? ShownSize(template, subject)
            : null;

    /// <summary>
    /// After a successful set-up, on a Tiny item: the client card at its real size and the art at
    /// the crop the game's board gives a Tiny item (<c>ItemVisualsController.SetIsTiny</c>). The
    /// game's set-up uses the template's size and turns the keyword off on the fresh material.
    /// </summary>
    public static void ShowTiny(CardPreviewBase card, ECardSize size)
    {
        if (card._clientCard != null)
            card._clientCard.Size = size;
        if (card._cardMaterial != null)
            card._cardMaterial.EnableKeyword(TinyArtKeyword);
    }

    /// <summary>Before the set-up: the instance's values, for after it.</summary>
    public static Kept Keep(TCardInstance instance) => new(instance.Attributes);

    /// <summary>
    /// The detached text card the tooltip reads: the template's tiers up to its starting tier,
    /// as the prefab's <c>UpdateInstanceFromTierData</c>, then the subject's tier and enchantment
    /// values, then its saved values over them, as the set-up and <see cref="Kept.ShowOn"/> do.
    /// </summary>
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

    /// <summary>
    /// An instance's values, kept through the set-up. Its only caller sets up a fresh instance
    /// each time, so unlike the desktop's it puts nothing back into the instance.
    /// </summary>
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

        /// <summary>
        /// Writes the kept values over <paramref name="target"/>. Returns whether anything was
        /// written.
        /// </summary>
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

        /// <summary>
        /// After a successful set-up: the values over the card's client card, and an item's gems
        /// drawn again from them, as the set-up drew them from the template's.
        /// </summary>
        public void ShowOn(CardPreviewBase card)
        {
            var client = card._clientCard;
            if (client != null && WriteOver(client.Attributes))
                card.TryCast<CardPreviewItem>()
                    ?._gemGroupController?.Initialize(client, null, false);
        }
    }
}
