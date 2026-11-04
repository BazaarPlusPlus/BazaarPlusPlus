#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards.CardRental;
using TheBazaar.UI;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>
/// A card BPP sets up from a template, the native face or the detached text card, shows the
/// Saved Card Values its instance or subject carries, written over the template's tier and
/// enchantment values. The game's <c>CardPreviewBase.SetUp</c> adopts the instance's attribute
/// dictionary, writes the starting tier into it, then gives the client card a new dictionary with
/// the tier's and the enchantment's values, so the saved values go on after it: a caller takes
/// <see cref="Keep"/> before it runs the set-up and calls <see cref="Kept.ShowOn"/> after a
/// successful one.
/// </summary>
internal static partial class NativeCardSetUp
{
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
        var tier = DesktopCardFacets.Tier.TryToSource(subject.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        var attributes = Values(subject.Attributes);
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
        return new TCardInstanceItem
        {
            TemplateId = template.Id,
            TemplateVersion = string.Empty,
            InstanceId = instanceId,
            Tier = tier,
            Attributes = attributes,
            EnchantmentType =
                subject.Enchantment is { } enchantment
                && DesktopCardFacets.Enchantment.TryToSource(enchantment, out var native)
                    ? native
                    : null,
        };
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
        var tier = DesktopCardFacets.Tier.TryToSource(subject.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        card.Attributes = new Dictionary<ECardAttributeType, int>();
        var tierData = (template as IHasTierData);
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
        var attributes = TheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            cardTemplate,
            tier
        );
        var item = (card as ItemCard);
        var itemTemplate = (template as TCardItem);
        if (item != null && itemTemplate != null)
            TheBazaar.CardExtensions.ApplyPreviewEnchantment(
                itemTemplate,
                item,
                attributes,
                subject.Enchantment is { } enchantment
                && DesktopCardFacets.Enchantment.TryToSource(enchantment, out var native)
                    ? new Nullable<EEnchantmentType>(native)
                    : new Nullable<EEnchantmentType>()
            );
        foreach (var pair in Values(subject.Attributes))
            attributes[pair.Key] = pair.Value;
        card.Attributes = attributes;
        card.Size = template.Size;
        card.Tags = template.Tags;
    }

    /// <summary>An instance's values, kept through the set-up.</summary>
    internal sealed class Kept
    {
        private readonly Dictionary<ECardAttributeType, int>? _instance;
        private readonly Dictionary<ECardAttributeType, int> _values;

        internal Kept(Dictionary<ECardAttributeType, int>? instance)
        {
            _instance = instance;
            _values = instance != null ? new(instance) : new();
        }

        /// <summary>
        /// Puts the kept values back into the instance's own dictionary, which the set-up wrote the
        /// starting tier into, so a board that renders the same instance again keys and shows the
        /// same values (<c>OwnedMonsterBoardPreview</c>, <c>NativeBoardSignature</c>), then writes
        /// them over <paramref name="target"/>. Returns whether anything was written.
        /// </summary>
        public bool WriteOver(IDictionary<ECardAttributeType, int>? target)
        {
            if (_values.Count == 0)
                return false;
            _instance!.Clear();
            foreach (var pair in _values)
                _instance[pair.Key] = pair.Value;
            if (target == null)
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
            if (WriteOver(card._clientCard?.Attributes))
                (card as CardPreviewItem)?._gemGroupController?.Initialize(card._clientCard);
        }
    }
}
