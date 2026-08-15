#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntitySnapshotReader
{
    internal static string ResolveDisplayName(Card card, string nativeTooltipTitle)
    {
        var name = ResolveTitle(card.Template?.Localization?.Title);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        if (!string.IsNullOrWhiteSpace(card.Template?.InternalName))
            return card.Template.InternalName;

        return CombatImpactEntityName.RemoveNativeEnchantmentPrefix(nativeTooltipTitle);
    }

    internal static IReadOnlyDictionary<string, CombatImpactEntity> Read(CombatSim simulation)
    {
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal);
        var order = 0;
        var cards = TheBazaar
            .Data.Entities.Values.Where(card => card?.InstanceId.Value is { Length: > 0 })
            .OrderBy(card =>
                card!.Owner?.CombatantId == ECombatantId.Opponent
                    ? ECombatantId.Opponent
                    : ECombatantId.Player
            )
            .ThenBy(card => card!.InstanceId.Value, StringComparer.Ordinal);
        foreach (var card in cards)
        {
            if (card?.InstanceId.Value is not { Length: > 0 } instanceId)
                continue;
            AddCard(entities, card, order++);
        }

        order = AddTransformedEntities(simulation, entities, order, CreateTransformedCard);

        var playerHero = NormalizeHero(TheBazaar.Data.Run?.Player?.Hero);
        AddPlayer(entities, ECombatantId.Player, playerHero, T("己方", "You"), order++);
        var opponentHero = NormalizeHero(TheBazaar.Data.Run?.Opponent?.Hero);
        var opponentName = TheBazaar.Data.SimPvpOpponent?.Name;
        if (string.IsNullOrWhiteSpace(opponentName))
            opponentName = ResolveOpponentName(opponentHero);
        AddPlayer(entities, ECombatantId.Opponent, opponentHero, opponentName, order);
        return entities;
    }

    internal static int AddTransformedEntities(
        CombatSim simulation,
        IDictionary<string, CombatImpactEntity> entities,
        int order,
        Func<SimEventCardTransformation, Card?> createCard
    )
    {
        foreach (var snapshot in CombatImpactTransformedCardSnapshotReader.Read(simulation))
            AddTransformedEntity(snapshot, entities, ref order, createCard);

        return order;
    }

    private static Card CreateTransformedCard(SimEventCardTransformation transformed)
    {
        try
        {
            return DTOUtils.CreateCard(
                transformed.InstanceId,
                transformed.TemplateId,
                transformed.Type
            );
        }
        catch
        {
            return transformed.Type switch
            {
                ECardType.Item => new ItemCard(),
                ECardType.Skill => new SkillCard(),
                ECardType.SocketEffect => new SocketEffect(),
                _ => new Card(),
            };
        }
    }

    private static void AddTransformedEntity(
        CombatImpactTransformedCardSnapshot snapshot,
        IDictionary<string, CombatImpactEntity> entities,
        ref int order,
        Func<SimEventCardTransformation, Card?> createCard
    )
    {
        var transformed = snapshot.Card;
        if (
            string.IsNullOrWhiteSpace(transformed.InstanceId)
            || entities.ContainsKey(transformed.InstanceId)
        )
            return;

        var card = createCard(transformed);
        if (card == null)
            return;

        card.InstanceId = InstanceId.TryParse(transformed.InstanceId);
        if (Guid.TryParse(transformed.TemplateId, out var templateId))
            card.TemplateId = templateId;
        card.Type = transformed.Type;
        if (snapshot.Update != null)
            card.Update(snapshot.Update);
        card.LeftSocketId = transformed.Socket ?? card.LeftSocketId;
        card.Section = transformed.Section ?? card.Section;

        AddCard(entities, card, order++, transformed.CombatantId);
    }

    private static void AddCard(
        IDictionary<string, CombatImpactEntity> entities,
        Card card,
        int order,
        ECombatantId? combatantOverride = null
    )
    {
        if (card.InstanceId.Value is not { Length: > 0 } instanceId)
            return;

        var template = card.Template;
        var name = ResolveTitle(template?.Localization?.Title);
        if (string.IsNullOrWhiteSpace(name))
            name = template?.InternalName;
        if (string.IsNullOrWhiteSpace(name))
            name = card.Type == ECardType.Skill ? T("技能", "Skill") : T("物品", "Item");

        var item = card as ItemCard;
        var effectAttributes = ReadEffectAttributeTypes(card, item);
        var activeEffects = ReadActiveEffects(card);
        var hiddenTags = CombatImpactEntityTags.ResolveHiddenTags(card);
        var activeAbilities = CombatImpactActiveAbilityReader.Read(item, activeEffects.Abilities);
        var abilityAttributeModifiers = CombatImpactAbilityAttributeModifierReader.Read(
            activeAbilities
        );
        entities[instanceId] = new CombatImpactEntity(
            instanceId,
            name!,
            card.Type.ToString(),
            null,
            order,
            card.TemplateId,
            card.Tier,
            card.Type is ECardType.Item or ECardType.SocketEffect
                ? CardSizeSpan.Resolve(card.Size)
                : 1,
            item?.Enchantment,
            card.Attributes == null
                ? null
                : new Dictionary<ECardAttributeType, int>(card.Attributes),
            combatantOverride ?? card.Owner?.CombatantId,
            effectAttributes.Abilities,
            effectAttributes.Auras,
            effectAttributes.ReferenceValuedAuraEffectIds,
            card.LeftSocketId,
            hiddenTags,
            card.Section,
            CombatImpactEntityTags.ResolveTags(card),
            CombatImpactAttributionRuleReader.ReadSourceRules(activeAbilities, activeEffects.Auras),
            card.Type == ECardType.SocketEffect
                ? CombatImpactAttributionRuleReader.ReadUseRules(activeAbilities)
                : null,
            abilityAttributeModifiers,
            CombatImpactCriticalTriggerReader.Read(activeAbilities),
            CombatImpactCriticalCapability.ReadCritCapableEffectIds(
                card.Type,
                activeAbilities,
                hiddenTags
            )
        );
    }

    private static ActiveEffects ReadActiveEffects(Card card)
    {
        try
        {
            if (card.Template is IHasTierData tiered)
            {
                return new ActiveEffects(
                    tiered.GetAbilityTemplatesByTier(card.Tier),
                    tiered.GetAuraTemplatesByTier(card.Tier)
                );
            }
        }
        catch
        {
            // Fall through to the complete template graph when tier data is incomplete.
        }

        return new ActiveEffects(card.Template?.Abilities?.Values, card.Template?.Auras?.Values);
    }

    private static EffectAttributeTypes ReadEffectAttributeTypes(Card card, ItemCard? item)
    {
        try
        {
            var abilities = new Dictionary<string, ECardAttributeType>(StringComparer.Ordinal);
            var auras = new Dictionary<string, ECardAttributeType>(StringComparer.Ordinal);
            var ambiguousAbilities = new HashSet<string>(StringComparer.Ordinal);
            var ambiguousAuras = new HashSet<string>(StringComparer.Ordinal);
            var referenceValuedAuras = new Dictionary<string, bool>(StringComparer.Ordinal);
            var ambiguousReferenceValuedAuras = new HashSet<string>(StringComparer.Ordinal);

            AddAbilityAttributeTypes(
                card.Template?.Abilities?.Values,
                abilities,
                ambiguousAbilities
            );
            AddAuraAttributeTypes(
                card.Template?.Auras?.Values,
                auras,
                ambiguousAuras,
                referenceValuedAuras,
                ambiguousReferenceValuedAuras
            );

            if (
                item?.Enchantment is { } enchantmentType
                && item.GetEnchantments() is { } enchantments
                && enchantments.TryGetValue(enchantmentType, out var enchantment)
            )
            {
                AddAbilityAttributeTypes(
                    enchantment.Abilities?.Values,
                    abilities,
                    ambiguousAbilities
                );
                AddAuraAttributeTypes(
                    enchantment.Auras?.Values,
                    auras,
                    ambiguousAuras,
                    referenceValuedAuras,
                    ambiguousReferenceValuedAuras
                );
            }

            var referenceValuedAuraEffectIds = referenceValuedAuras
                // Base and enchantment auras can reuse an effect id for different attributes.
                // The attribute mapping is then ambiguous, but the formula classification is
                // still exact when every direct aura action under that id is reference-valued.
                .Where(item => item.Value)
                .Select(item => item.Key)
                .ToArray();

            return new EffectAttributeTypes(
                abilities.Count == 0 ? null : abilities,
                auras.Count == 0 ? null : auras,
                referenceValuedAuraEffectIds.Length == 0 ? null : referenceValuedAuraEffectIds
            );
        }
        catch
        {
            return default;
        }
    }

    private static void AddAbilityAttributeTypes(
        IEnumerable<TCardAbility>? abilities,
        IDictionary<string, ECardAttributeType> destination,
        ISet<string> ambiguousEffectIds
    )
    {
        if (abilities == null)
            return;

        foreach (var ability in abilities)
        {
            // A compound action can modify multiple attributes under one effect id, so only
            // direct modifiers provide an exact effect-to-attribute mapping.
            if (ability?.Action is not TActionCardModifyAttribute modifier)
                continue;
            AddUniqueEffectAttribute(
                ability.Id,
                modifier.AttributeType,
                destination,
                ambiguousEffectIds
            );
        }
    }

    private static void AddAuraAttributeTypes(
        IEnumerable<TCardAura>? auras,
        IDictionary<string, ECardAttributeType> destination,
        ISet<string> ambiguousEffectIds,
        IDictionary<string, bool> referenceValuedEffects,
        ISet<string> ambiguousReferenceValuedEffects
    )
    {
        if (auras == null)
            return;

        foreach (var aura in auras)
        {
            // Keep the same direct-only rule as abilities; nested aura actions are not unique.
            if (aura?.Action is not TAuraActionCardModifyAttribute modifier)
                continue;
            AddUniqueEffectAttribute(
                aura.Id,
                modifier.AttributeType,
                destination,
                ambiguousEffectIds
            );
            AddUniqueEffectFlag(
                aura.Id,
                modifier.Value is ITReferenceValue,
                referenceValuedEffects,
                ambiguousReferenceValuedEffects
            );
        }
    }

    private static void AddUniqueEffectFlag(
        string? effectId,
        bool value,
        IDictionary<string, bool> destination,
        ISet<string> ambiguousEffectIds
    )
    {
        if (string.IsNullOrWhiteSpace(effectId) || ambiguousEffectIds.Contains(effectId!))
            return;
        if (!destination.TryGetValue(effectId!, out var existing))
        {
            destination[effectId!] = value;
            return;
        }
        if (existing == value)
            return;

        destination.Remove(effectId!);
        ambiguousEffectIds.Add(effectId!);
    }

    private static void AddUniqueEffectAttribute(
        string? effectId,
        ECardAttributeType attributeType,
        IDictionary<string, ECardAttributeType> destination,
        ISet<string> ambiguousEffectIds
    )
    {
        if (string.IsNullOrWhiteSpace(effectId) || ambiguousEffectIds.Contains(effectId!))
            return;
        if (!destination.TryGetValue(effectId!, out var existing))
        {
            destination[effectId!] = attributeType;
            return;
        }
        if (existing == attributeType)
            return;

        destination.Remove(effectId!);
        ambiguousEffectIds.Add(effectId!);
    }

    private static void AddPlayer(
        Dictionary<string, CombatImpactEntity> entities,
        ECombatantId combatant,
        EHero? hero,
        string name,
        int order
    )
    {
        var id = CombatImpactProjector.PlayerId(combatant);
        entities[id] = new CombatImpactEntity(
            id,
            name,
            "Hero",
            hero,
            order,
            CombatantId: combatant
        );
    }

    private static EHero? NormalizeHero(EHero? hero) =>
        hero.HasValue && hero.Value != EHero.Common ? hero : null;

    private static string ResolveOpponentName(EHero? hero)
    {
        if (!hero.HasValue)
            return T("对手", "Opponent");

        var localized = TheDragonsHeroIdentity.ResolveDisplayName(hero.Value);
        return
            IsChinese() && string.Equals(localized, hero.Value.ToString(), StringComparison.Ordinal)
            ? "对手"
            : localized;
    }

    private static string T(string chinese, string english) => IsChinese() ? chinese : english;

    private static bool IsChinese() =>
        L.CurrentLanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveTitle(BazaarGameShared.Domain.Core.TLocalizableText? localizable)
    {
        if (localizable == null)
            return null;
        try
        {
            var localized = TooltipExtensions.GetLocalizedText(localizable);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch
        {
            // Localization may not be initialized during early replay bootstrap.
        }

        return !string.IsNullOrWhiteSpace(localizable.Text) ? localizable.Text : localizable.Key;
    }

    private readonly record struct EffectAttributeTypes(
        IReadOnlyDictionary<string, ECardAttributeType>? Abilities,
        IReadOnlyDictionary<string, ECardAttributeType>? Auras,
        IReadOnlyCollection<string>? ReferenceValuedAuraEffectIds
    );

    private readonly record struct ActiveEffects(
        IEnumerable<TCardAbility>? Abilities,
        IEnumerable<TCardAura>? Auras
    );
}
