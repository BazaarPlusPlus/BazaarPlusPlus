#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Values;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class UpgradePreviewValueProjection
{
    private UpgradePreviewValueProjection(
        ItemCard card,
        ItemCard sourceCard,
        ITCard template,
        ValueContext valueContext
    )
    {
        Card = card;
        SourceCard = sourceCard;
        Template = template;
        ValueContext = valueContext;
    }

    internal ItemCard Card { get; }

    internal ItemCard SourceCard { get; }

    internal ITCard Template { get; }

    internal ValueContext ValueContext { get; }

    internal static bool TryCreate(
        ItemCard source,
        ITCard template,
        ValueContext sourceValueContext,
        out UpgradePreviewValueProjection projection
    )
    {
        projection = null!;
        if (source == null || template is not TCardItem itemTemplate)
            return false;

        var nextTier = source.GetNextTier();
        if (
            nextTier == source.Tier
            || !itemTemplate.TryGetTierTemplate(nextTier, out var next)
            || next == null
        )
            return false;

        var attributes = new Dictionary<ECardAttributeType, int>(source.Attributes);
        foreach (var attribute in source.Attributes.Keys.Concat(next.Attributes.Keys).Distinct())
        {
            var currentBase = itemTemplate.GetAttributeBaseValueAtTier(attribute, source.Tier);
            var nextBase = itemTemplate.GetAttributeBaseValueAtTier(attribute, nextTier);
            if (!nextBase.HasValue)
                continue;

            if (source.Attributes.TryGetValue(attribute, out var currentValue))
            {
                attributes[attribute] = UpgradePreviewValueMath.ProjectAttribute(
                    currentValue,
                    currentBase,
                    nextBase.Value
                );
            }
            else
            {
                attributes[attribute] = nextBase.Value;
            }
        }

        var projectedCard = new ItemCard
        {
            InstanceId = source.InstanceId,
            TemplateId = source.TemplateId,
            Attributes = attributes,
            Heroes = new HashSet<EHero>(source.Heroes),
            HiddenTags = new HashSet<EHiddenTag>(source.HiddenTags),
            Size = source.Size,
            Tags = new HashSet<ECardTag>(source.Tags),
            Tier = nextTier,
            Type = source.Type,
            Owner = source.Owner,
            LeftSocketId = source.LeftSocketId,
            Section = source.Section,
            State = source.State,
            Template = template,
            Enchantment = source.Enchantment,
        };

        ApplyRuntimeMultipliers(source, projectedCard, itemTemplate, nextTier, sourceValueContext);

        projection = new UpgradePreviewValueProjection(
            projectedCard,
            source,
            template,
            new ValueContext(sourceValueContext.Run, projectedCard, sourceValueContext.EventContext)
        );
        return true;
    }

    internal bool TryResolve(ITooltipComponent? component, out float value)
    {
        value = default;
        if (component is not ITooltipToken token)
            return false;

        var context = new TooltipContext(Card, Template, ValueContext);
        ITooltipToken? projectedToken = token switch
        {
            TooltipComponentAttribute attribute when attribute.ReferencedAttribute.HasValue =>
                TooltipComponentAttribute.Create(
                    context,
                    attribute.ReferencedAttribute.Value.ToString(),
                    attribute.StartWordIndex
                ),
            TooltipComponentAbility ability => TooltipComponentAbility.Create(
                context,
                ability.EffectId,
                ability.Accessor,
                ability.StartWordIndex
            ),
            TooltipComponentAura aura => TooltipComponentAura.Create(
                context,
                aura.EffectId,
                aura.Accessor,
                aura.StartWordIndex
            ),
            _ => null,
        };

        var resolved = projectedToken?.Resolve();
        if (!resolved.HasValue)
            return false;

        value = resolved.Value;
        return true;
    }

    internal bool TryResolveEffectiveCooldowns(
        out float currentSeconds,
        out float upgradedSeconds
    ) =>
        UpgradePreviewValueMath.TryEffectiveCooldownSeconds(
            SourceCard.Attributes.TryGetValue(ECardAttributeType.CooldownMax, out var currentMs)
                ? currentMs
                : null,
            Card.Attributes.TryGetValue(ECardAttributeType.CooldownMax, out var upgradedMs)
                ? upgradedMs
                : null,
            out currentSeconds,
            out upgradedSeconds
        );

    private static void ApplyRuntimeMultipliers(
        ItemCard source,
        ItemCard projected,
        TCardItem template,
        ETier nextTier,
        ValueContext sourceValueContext
    )
    {
        var auraMultipliers = UpgradePreviewAttributeMultiplierResolver.Resolve(
            source,
            projected,
            sourceValueContext
        );
        foreach (var attribute in projected.Attributes.Keys.ToArray())
        {
            var currentBase = template.GetAttributeBaseValueAtTier(attribute, source.Tier);
            var nextBase = template.GetAttributeBaseValueAtTier(attribute, nextTier);
            if (!nextBase.HasValue)
                continue;

            var multipliers = UpgradePreviewMultipliers.Identity;
            if (attribute == ECardAttributeType.CooldownMax)
            {
                multipliers = multipliers.Times(
                    new UpgradePreviewMultipliers(
                        ResolveCooldownMultiplier(source),
                        ResolveCooldownMultiplier(projected)
                    )
                );
            }

            if (auraMultipliers.TryGetValue(attribute, out var auraMultiplier))
                multipliers = multipliers.Times(auraMultiplier);

            if (
                UpgradePreviewValueMath.TryApplyMultipliers(
                    currentBase.HasValue
                    && source.Attributes.TryGetValue(attribute, out var currentValue)
                        ? currentValue
                        : null,
                    currentBase,
                    nextBase.Value,
                    multipliers,
                    out var projectedValue
                )
            )
                projected.Attributes[attribute] = projectedValue;
        }
    }

    private static float ResolveCooldownMultiplier(ItemCard card) =>
        UpgradePreviewValueMath.CooldownMultiplier(
            card.Attributes.GetValueOrDefault(ECardAttributeType.PercentCooldownReduction)
        );
}
