#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Combat;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

public readonly record struct PeriodicImpactKey(
    string SourceId,
    Combatant Combatant,
    CombatImpactPeriodicKind Kind
);

public sealed record CombatImpactPrerequisiteSkillSourceRule(
    string EffectId,
    Guid SkillTemplateId,
    IReadOnlyCollection<CardTier> SkillTiers,
    string SourceTypeLabel = "Skill"
)
{
    internal bool Matches(CombatImpactEntity entity) =>
        entity.TypeLabel == SourceTypeLabel
        && (SourceTypeLabel != "Item" || entity.Section == CardSection.Hand)
        && entity.TemplateId == SkillTemplateId
        && (SkillTiers.Count == 0 || SkillTiers.Contains(entity.Tier));
}

public sealed record CombatImpactItemTagCondition(
    CombatListComparison Operator,
    IReadOnlyCollection<CardTag>? PublicTags = null,
    IReadOnlyCollection<CardHiddenTag>? HiddenTags = null,
    IReadOnlyList<CombatImpactItemTagCondition>? AllOf = null
)
{
    internal bool Matches(CombatImpactEntity entity)
    {
        if (AllOf is { Count: > 0 })
            return AllOf.All(condition => condition.Matches(entity));
        if (PublicTags is { Count: > 0 })
            return Matches(PublicTags, entity.Tags);
        if (HiddenTags is { Count: > 0 })
            return Matches(HiddenTags, entity.HiddenTags);
        return false;
    }

    private bool Matches<T>(IReadOnlyCollection<T> required, IReadOnlyCollection<T>? actual)
        where T : struct, Enum
    {
        var present = actual == null ? new HashSet<T>() : new HashSet<T>(actual);
        return Operator switch
        {
            CombatListComparison.All => required.All(present.Contains),
            CombatListComparison.Any => required.Any(present.Contains),
            CombatListComparison.None => required.All(tag => !present.Contains(tag)),
            _ => false,
        };
    }
}

public sealed record CombatImpactUseAttributionRule(
    CombatImpactPrerequisiteSkillSourceRule SourceRule,
    CombatImpactItemTagCondition ItemCondition,
    int FixedTempoAmount
);

/// <summary>
/// The producer's ledger entity (ADR-0019): the facts that attribution reads (attributes, tags,
/// modifiers, rules) and the native card preview renders (hero, tier, enchantment), in BPP
/// vocabulary. A host adapter builds it from its game build's card templates.
/// </summary>
public sealed record CombatImpactEntity(
    string Id,
    string Name,
    string TypeLabel,
    CardHero? Hero,
    int Order,
    Guid TemplateId = default,
    CardTier Tier = CardTier.Bronze,
    int DisplaySpan = 1,
    CardEnchantment? EnchantmentType = null,
    IReadOnlyDictionary<CombatCardAttribute, int>? Attributes = null,
    Combatant? CombatantId = null,
    IReadOnlyDictionary<string, CombatCardAttribute>? AbilityAttributeTypesByEffectId = null,
    IReadOnlyDictionary<string, CombatCardAttribute>? AuraAttributeTypesByEffectId = null,
    IReadOnlyCollection<string>? ReferenceValuedAuraEffectIds = null,
    CardSocket? SocketId = null,
    IReadOnlyCollection<CardHiddenTag>? HiddenTags = null,
    CardSection? Section = null,
    IReadOnlyCollection<CardTag>? Tags = null,
    IReadOnlyDictionary<
        string,
        CombatImpactPrerequisiteSkillSourceRule
    >? PrerequisiteSkillSourceRulesByEffectId = null,
    IReadOnlyCollection<CombatImpactUseAttributionRule>? UseAttributionRules = null,
    IReadOnlyDictionary<string, CombatAttributeModifier>? AbilityAttributeModifiersByEffectId =
        null,
    IReadOnlyDictionary<string, CombatEffectPriority>? CriticalTriggerAbilitiesByEffectId = null,
    IReadOnlyCollection<string>? CritCapableEffectIds = null,
    IReadOnlyDictionary<string, CombatAttributeModifier>? AuraAttributeModifiersByEffectId = null
) : CombatImpactLedgerEntity(Id, Name, TypeLabel, Order);

public static class CombatImpactTags
{
    public static IReadOnlyCollection<CardTag>? Merge(
        IReadOnlyCollection<CardTag>? runtime,
        IReadOnlyCollection<CardTag>? template,
        IReadOnlyCollection<CardTag>? enchantment
    )
    {
        var tags = new HashSet<CardTag>();
        if (runtime != null)
            tags.UnionWith(runtime);
        if (template != null)
            tags.UnionWith(template);
        if (enchantment != null)
            tags.UnionWith(enchantment);
        return tags.Count == 0 ? null : tags.ToArray();
    }
}

public static class CombatImpactHiddenTags
{
    public static IReadOnlyCollection<CardHiddenTag>? Merge(
        IReadOnlyCollection<CardHiddenTag>? runtime,
        IReadOnlyCollection<CardHiddenTag>? template,
        IReadOnlyCollection<CardHiddenTag>? enchantment
    )
    {
        var hiddenTags = new HashSet<CardHiddenTag>();
        if (runtime != null)
            hiddenTags.UnionWith(runtime);
        if (template != null)
            hiddenTags.UnionWith(template);
        if (enchantment != null)
            hiddenTags.UnionWith(enchantment);
        return hiddenTags.Count == 0 ? null : hiddenTags.ToArray();
    }
}
