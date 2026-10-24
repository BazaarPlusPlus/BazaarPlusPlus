using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Cards.Skill;
using Il2CppBazaarGameShared.Domain.Core;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Effect.Actions;
using Il2CppBazaarGameShared.Domain.Effect.AuraActions;
using Il2CppBazaarGameShared.Domain.Effect.Trigger;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The Android host's projection of a live IL2CPP card template into the Core
/// <see cref="CollectionCardVm"/>, the counterpart of the desktop's
/// <c>CollectionCardVmProjection</c>: the same eligibility (<see cref="CollectionCardEligibility"/>),
/// the same derived Quest and Lifesteal keywords, the same Multicast and Destroy mechanics walk,
/// and the game's own localized title, description and tooltips. One difference: the
/// <c>{ability.*}</c> placeholders drop instead of resolving, because the ability value reader is
/// desktop-only; they only feed search, the card itself is the game's.
/// <para>Proxies never satisfy <c>is</c>; every type test goes through <c>TryCast</c>.</para>
/// </summary>
internal static class AndroidCollectionCardProjection
{
    private static readonly ETier[] LookupTiers =
    [
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
        ETier.Legendary,
    ];

    /// <summary>Null when the template is not a catalog card or has a member BPP has no facet for.</summary>
    public static CollectionCardVm? From(TCardBase template, out bool eligible)
    {
        var nativeType = template.Type;
        eligible =
            CollectionCardEligibility.Classify(
                nativeType == ECardType.Item || nativeType == ECardType.Skill,
                template.SpawningEligibility == ESpawnEligibility.Never,
                template.ArtKey,
                template.InternalName
            ) == CollectionCardEligibilityReason.Accepted;
        if (!eligible)
            return null;
        if (
            !AndroidCardFacets.Type.TryToFacet(nativeType, out var type)
            || !AndroidCardFacets.Size.TryToFacet(template.Size, out var size)
            || !AndroidCardFacets.Tier.TryToFacet(template.StartingTier, out var startingTier)
        )
            return null;

        var item = nativeType == ECardType.Item ? new TCardItem(template.Pointer) : null;
        var skill = nativeType == ECardType.Skill ? new TCardSkill(template.Pointer) : null;
        var nativeHiddenTags = Materialize(template.HiddenTags);
        var isPackage = nativeHiddenTags.Contains(EHiddenTag.Package);
        AddDerivedKeywords(nativeHiddenTags, item);

        var titles = SearchTexts(template.Localization?.Title);
        var descriptions = SearchTexts(template.Localization?.Description);
        var tooltips = TooltipTexts(template);
        var displayName = titles.Count > 0 ? titles[0] : template.InternalName ?? string.Empty;
        var searchableContent = string.Join(" ", descriptions);
        if (tooltips.Count > 0)
            searchableContent = string.Concat(searchableContent, " ", string.Join(" ", tooltips));

        var id = Guid.Parse(template.Id.ToString());
        var heroes = AndroidCardFacets.Hero.ToFacetSet(Materialize(template.Heroes));
        var tags = AndroidCardFacets.Tag.ToFacetSet(Materialize(template.Tags));
        var hiddenTags = AndroidCardFacets.HiddenTag.ToFacetSet(nativeHiddenTags);
        var mechanics = ProjectMechanics(template, nativeHiddenTags, item, skill);
        var enchantments = item == null ? Empty : ProjectEnchantments(item);
        var internalName = template.InternalName ?? string.Empty;
        var artKey = template.ArtKey ?? string.Empty;

        return new CollectionCardVm
        {
            Id = id,
            Type = type,
            Size = size,
            StartingTier = startingTier,
            Heroes = heroes,
            Tags = tags,
            HiddenTags = hiddenTags,
            Mechanics = mechanics,
            DisplayName = displayName,
            Description = descriptions.Count > 0 ? descriptions[0] : string.Empty,
            InternalName = internalName,
            ArtKey = artKey,
            IsEnchantable = enchantments.Count > 0,
            Enchantments = enchantments,
            IsPackage = isPackage,
            SearchText = CollectionCardSearch.BuildCorpus(
                new CollectionCardVm
                {
                    Id = id,
                    Type = type,
                    Size = size,
                    StartingTier = startingTier,
                    Heroes = heroes,
                    Tags = tags,
                    HiddenTags = hiddenTags,
                    Mechanics = mechanics,
                    DisplayName = string.Join(" ", titles),
                    Description = searchableContent,
                    InternalName = internalName,
                    ArtKey = artKey,
                    IsEnchantable = enchantments.Count > 0,
                    Enchantments = enchantments,
                    IsPackage = isPackage,
                }
            ),
        };
    }

    private static readonly IReadOnlyDictionary<
        CardEnchantment,
        CollectionCardEnchantmentFacets
    > Empty = new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>();

    // The desktop's CollectionDerivedKeywordFacts: a quest item is Quest, and an item that heals
    // through Lifesteal at any tier is Lifesteal even without the hidden tag.
    private static void AddDerivedKeywords(HashSet<EHiddenTag> hiddenTags, TCardItem? item)
    {
        if (item == null)
            return;
        if (item.Quests?.Count > 0)
            hiddenTags.Add(EHiddenTag.Quest);
        if (hiddenTags.Contains(EHiddenTag.Lifesteal))
            return;
        foreach (var tier in LookupTiers)
        {
            if (BaseValue(item, null, ECardAttributeType.Lifesteal, tier) > 0)
            {
                hiddenTags.Add(EHiddenTag.Lifesteal);
                return;
            }
        }
    }

    // The desktop's CollectionMechanicFacts over the interop proxies.
    private static CollectionMechanic ProjectMechanics(
        TCardBase template,
        HashSet<EHiddenTag> nativeHiddenTags,
        TCardItem? item,
        TCardSkill? skill
    )
    {
        var facts = CollectionMechanic.None;
        foreach (var hiddenTag in nativeHiddenTags)
            if (
                AndroidCardFacets.HiddenTag.TryToFacet(hiddenTag, out var facet)
                && CollectionMechanics.TryFromHiddenTag(facet, out var mechanic)
            )
                facts |= mechanic;

        var tiers = item?.Tiers ?? skill?.Tiers;
        if (tiers == null)
            return facts;
        var abilityIds = new HashSet<string>(StringComparer.Ordinal);
        var auraIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in tiers)
        {
            var tier = pair.Key;
            if (BaseValue(item, skill, ECardAttributeType.Multicast, tier) > 1)
                facts |= CollectionMechanic.Multicast;
            if (BaseValue(item, skill, ECardAttributeType.DestroyImmunity, tier) > 0)
                facts |= CollectionMechanic.Destroy;
            var tierTemplate = pair.Value;
            if (tierTemplate == null)
                continue;
            if (tierTemplate.AbilityIds != null)
                foreach (var abilityId in tierTemplate.AbilityIds)
                    abilityIds.Add(abilityId);
            if (tierTemplate.AuraIds != null)
                foreach (var auraId in tierTemplate.AuraIds)
                    auraIds.Add(auraId);
        }

        const CollectionMechanic all = CollectionMechanic.Multicast | CollectionMechanic.Destroy;
        var abilities = template.Abilities;
        if (abilities != null)
            foreach (var abilityId in abilityIds)
            {
                if ((facts & all) == all)
                    break;
                if (!abilities.ContainsKey(abilityId))
                    continue;
                var ability = abilities[abilityId];
                if (ability == null)
                    continue;
                facts |= ActionFacts(ability.Action);
                facts |= TriggerFacts(ability.Trigger);
            }

        var auras = template.Auras;
        if (!facts.Has(CollectionMechanic.Multicast) && auras != null)
            foreach (var auraId in auraIds)
            {
                if (!auras.ContainsKey(auraId))
                    continue;
                var modifier = auras[auraId]?.Action?.TryCast<TAuraActionCardModifyAttribute>();
                if (modifier != null && modifier.AttributeType == ECardAttributeType.Multicast)
                {
                    facts |= CollectionMechanic.Multicast;
                    break;
                }
            }
        return facts;
    }

    private static CollectionMechanic ActionFacts(ITAction? action)
    {
        if (action == null)
            return CollectionMechanic.None;
        var modifier = action.TryCast<TActionCardModifyAttribute>();
        if (modifier != null)
            return modifier.AttributeType == ECardAttributeType.Multicast
                ? CollectionMechanic.Multicast
                : CollectionMechanic.None;
        // The destroy cluster; abilities nested in TActionCardTransformDestroyed belong to the
        // replacement card and are never walked, as on desktop.
        if (
            action.TryCast<TActionCardDestroy>() != null
            || action.TryCast<TActionCardRepair>() != null
            || action.TryCast<TActionCardTransformDestroyed>() != null
        )
            return CollectionMechanic.Destroy;
        var combined = action.TryCast<TActionAnd>();
        if (combined?.Actions == null)
            return CollectionMechanic.None;
        var facts = CollectionMechanic.None;
        foreach (var child in combined.Actions)
            facts |= ActionFacts(child);
        return facts;
    }

    private static CollectionMechanic TriggerFacts(TTriggerBase? trigger)
    {
        if (trigger == null)
            return CollectionMechanic.None;
        if (
            trigger.TryCast<TTriggerOnBeforeCardDestroyed>() != null
            || trigger.TryCast<TTriggerOnCardDestroyed>() != null
            || trigger.TryCast<TTriggerOnCardPerformedDestruction>() != null
        )
            return CollectionMechanic.Destroy;
        var combined = trigger.TryCast<TTriggerOr>();
        if (combined?.Triggers == null)
            return CollectionMechanic.None;
        var facts = CollectionMechanic.None;
        foreach (var child in combined.Triggers)
            facts |= TriggerFacts(child);
        return facts;
    }

    private static int? BaseValue(
        TCardItem? item,
        TCardSkill? skill,
        ECardAttributeType attribute,
        ETier tier
    ) =>
        item != null
            ? AndroidGameIds.Unboxed(() => item.GetAttributeBaseValueAtTier(attribute, tier))
        : skill != null
            ? AndroidGameIds.Unboxed(() => skill.GetAttributeBaseValueAtTier(attribute, tier))
        : null;

    private static IReadOnlyDictionary<
        CardEnchantment,
        CollectionCardEnchantmentFacets
    > ProjectEnchantments(TCardItem item)
    {
        var source = item.Enchantments;
        if (source == null || source.Count == 0)
            return Empty;
        var result = new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>(source.Count);
        foreach (var pair in source)
        {
            if (!AndroidCardFacets.Enchantment.TryToFacet(pair.Key, out var type))
                continue;
            var enchantment = pair.Value;
            result[type] = new CollectionCardEnchantmentFacets(
                type,
                AndroidCardFacets.Tag.ToFacetSet(Materialize(enchantment?.Tags)),
                AndroidCardFacets.HiddenTag.ToFacetSet(Materialize(enchantment?.HiddenTags))
            );
        }
        return result;
    }

    private static List<string> TooltipTexts(TCardBase template)
    {
        var values = new List<string>();
        var tooltips = template.Localization?.Tooltips;
        if (tooltips == null)
            return values;
        foreach (var tooltip in tooltips)
            AddTexts(values, tooltip?.Content);
        return values;
    }

    private static List<string> SearchTexts(TLocalizableText? text)
    {
        var values = new List<string>(2);
        AddTexts(values, text);
        return values;
    }

    // The current language's text first, then the authored English, each once.
    private static void AddTexts(List<string> values, TLocalizableText? text)
    {
        if (text == null)
            return;
        Add(values, Localized(text));
        Add(values, text.Text);
    }

    private static void Add(List<string> values, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var value = CollectionAbilityPlaceholders.Format(text, NoAbilityValue);
        if (value != null)
            value = CollectionAbilityPlaceholders.DropAuraPlaceholders(value);
        if (string.IsNullOrWhiteSpace(value) || values.Contains(value!, StringComparer.Ordinal))
            return;
        values.Add(value!);
    }

    private static bool NoAbilityValue(string abilityId, out string valueText, out string? unit)
    {
        valueText = string.Empty;
        unit = null;
        return false;
    }

    private static string? Localized(TLocalizableText text)
    {
        try
        {
            return Il2CppTheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static HashSet<T> Materialize<T>(Il2CppSystem.Collections.Generic.HashSet<T>? source)
        where T : unmanaged
    {
        var values = new HashSet<T>();
        if (source == null)
            return values;
        foreach (var value in source)
            values.Add(value);
        return values;
    }
}
