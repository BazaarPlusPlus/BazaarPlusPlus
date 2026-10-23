#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal readonly record struct PeriodicImpactKey(
    string SourceId,
    ECombatantId Combatant,
    CombatImpactPeriodicKind Kind
);

internal sealed record CombatImpactPrerequisiteSkillSourceRule(
    string EffectId,
    Guid SkillTemplateId,
    IReadOnlyCollection<ETier> SkillTiers,
    string SourceTypeLabel = "Skill"
)
{
    internal bool Matches(CombatImpactEntity entity) =>
        entity.TypeLabel == SourceTypeLabel
        && (SourceTypeLabel != "Item" || entity.Section == EInventorySection.Hand)
        && entity.TemplateId == SkillTemplateId
        && (SkillTiers.Count == 0 || SkillTiers.Contains(entity.Tier));
}

internal sealed record CombatImpactItemTagCondition(
    EListComparisonOperator Operator,
    IReadOnlyCollection<ECardTag>? PublicTags = null,
    IReadOnlyCollection<EHiddenTag>? HiddenTags = null,
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
            EListComparisonOperator.All => required.All(present.Contains),
            EListComparisonOperator.Any => required.Any(present.Contains),
            EListComparisonOperator.None => required.All(tag => !present.Contains(tag)),
            _ => false,
        };
    }
}

internal sealed record CombatImpactUseAttributionRule(
    CombatImpactPrerequisiteSkillSourceRule SourceRule,
    CombatImpactItemTagCondition ItemCondition,
    int FixedTempoAmount
);

/// <summary>
/// The desktop producer's ledger entity: the game-typed facts that attribution reads (attributes,
/// tags, modifiers, rules) and the native card preview renders (hero, tier, enchantment).
/// </summary>
internal sealed record CombatImpactEntity(
    string Id,
    string Name,
    string TypeLabel,
    EHero? Hero,
    int Order,
    Guid TemplateId = default,
    ETier Tier = ETier.Bronze,
    int DisplaySpan = 1,
    EEnchantmentType? EnchantmentType = null,
    IReadOnlyDictionary<ECardAttributeType, int>? Attributes = null,
    ECombatantId? CombatantId = null,
    IReadOnlyDictionary<string, ECardAttributeType>? AbilityAttributeTypesByEffectId = null,
    IReadOnlyDictionary<string, ECardAttributeType>? AuraAttributeTypesByEffectId = null,
    IReadOnlyCollection<string>? ReferenceValuedAuraEffectIds = null,
    EContainerSocketId? SocketId = null,
    IReadOnlyCollection<EHiddenTag>? HiddenTags = null,
    EInventorySection? Section = null,
    IReadOnlyCollection<ECardTag>? Tags = null,
    IReadOnlyDictionary<
        string,
        CombatImpactPrerequisiteSkillSourceRule
    >? PrerequisiteSkillSourceRulesByEffectId = null,
    IReadOnlyCollection<CombatImpactUseAttributionRule>? UseAttributionRules = null,
    IReadOnlyDictionary<string, TActionCardModifyAttribute>? AbilityAttributeModifiersByEffectId =
        null,
    IReadOnlyDictionary<string, EEffectPriority>? CriticalTriggerAbilitiesByEffectId = null,
    IReadOnlyCollection<string>? CritCapableEffectIds = null,
    IReadOnlyDictionary<string, TAuraActionCardModifyAttribute>? AuraAttributeModifiersByEffectId =
        null
) : CombatImpactLedgerEntity(Id, Name, TypeLabel, Order);

internal static class CombatImpactTags
{
    internal static IReadOnlyCollection<ECardTag>? Merge(
        IReadOnlyCollection<ECardTag>? runtime,
        IReadOnlyCollection<ECardTag>? template,
        IReadOnlyCollection<ECardTag>? enchantment
    )
    {
        var tags = new HashSet<ECardTag>();
        if (runtime != null)
            tags.UnionWith(runtime);
        if (template != null)
            tags.UnionWith(template);
        if (enchantment != null)
            tags.UnionWith(enchantment);
        return tags.Count == 0 ? null : tags.ToArray();
    }
}

internal static class CombatImpactHiddenTags
{
    internal static IReadOnlyCollection<EHiddenTag>? Merge(
        IReadOnlyCollection<EHiddenTag>? runtime,
        IReadOnlyCollection<EHiddenTag>? template,
        IReadOnlyCollection<EHiddenTag>? enchantment
    )
    {
        var hiddenTags = new HashSet<EHiddenTag>();
        if (runtime != null)
            hiddenTags.UnionWith(runtime);
        if (template != null)
            hiddenTags.UnionWith(template);
        if (enchantment != null)
            hiddenTags.UnionWith(enchantment);
        return hiddenTags.Count == 0 ? null : hiddenTags.ToArray();
    }
}
