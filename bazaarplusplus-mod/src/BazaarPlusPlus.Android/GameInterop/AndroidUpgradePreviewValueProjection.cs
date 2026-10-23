using BazaarPlusPlus.Game.Tooltips;
using Il2CppBazaarGameClient.Domain.Models;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameClient.Domain.Tooltips;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Interfaces;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Effect;
using Il2CppBazaarGameShared.Domain.Effect.AuraActions;
using Il2CppBazaarGameShared.Domain.Prerequisites;
using Il2CppBazaarGameShared.Domain.Runs;
using Il2CppBazaarGameShared.Domain.Targeting;
using Il2CppBazaarGameShared.Domain.Values;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The desktop's <c>UpgradePreviewValueProjection</c> over the IL2CPP game types: a copy of the
/// item at its next tier, carrying the runtime gains it has over its current tier's base values
/// and the out-of-combat multiplier auras of the cards around it, and the tooltip tokens
/// resolved again against that copy. The arithmetic is Core's
/// <see cref="UpgradePreviewValueMath"/>, shared with the desktop.
/// </summary>
internal sealed class AndroidUpgradePreviewValueProjection
{
    private readonly ItemCard _source;
    private readonly ItemCard _card;
    private readonly ITCard _template;
    private readonly ValueContext _valueContext;

    private AndroidUpgradePreviewValueProjection(
        ItemCard source,
        ItemCard card,
        ITCard template,
        IRun? run
    )
    {
        _source = source;
        _card = card;
        _template = template;
        _valueContext = new ValueContext(run!, card.TryCast<ICard>());
    }

    public ETier SourceTier => _source.Tier;

    public ETier ProjectedTier => _card.Tier;

    /// <summary>Why the last multiplier resolution failed, or null; read once by the caller.</summary>
    public Exception? MultiplierFailure { get; private set; }

    public static bool TryCreate(
        CardTooltipData tooltipData,
        out AndroidUpgradePreviewValueProjection projection
    )
    {
        projection = null!;
        var source = tooltipData.CardInstance?.TryCast<ItemCard>();
        var template = tooltipData.CardTemplate;
        var itemTemplate = template?.TryCast<TCardItem>();
        if (source == null || template == null || itemTemplate == null)
            return false;

        var currentTier = source.Tier;
        var nextTier = source.GetNextTier();
        if (
            nextTier == currentTier
            || !itemTemplate.TryGetTierTemplate(nextTier, out var next)
            || next == null
        )
            return false;

        var sourceAttributes = Read(source.Attributes);
        var attributes = new Dictionary<ECardAttributeType, int>(sourceAttributes);
        foreach (
            var attribute in sourceAttributes.Keys.Concat(Read(next.Attributes).Keys).Distinct()
        )
        {
            var currentBase = BaseValue(itemTemplate, attribute, currentTier);
            var nextBase = BaseValue(itemTemplate, attribute, nextTier);
            if (!nextBase.HasValue)
                continue;
            attributes[attribute] = sourceAttributes.TryGetValue(attribute, out var currentValue)
                ? UpgradePreviewValueMath.ProjectAttribute(
                    currentValue,
                    currentBase,
                    nextBase.Value
                )
                : nextBase.Value;
        }

        // The game's own copy keeps the section, socket owner, tags, and enchantment.
        var card = Il2CppTheBazaar.CardExtensions.CreateCopy(source)?.TryCast<ItemCard>();
        if (card == null)
            return false;
        card.Tier = nextTier;
        card.Heroes = source.Heroes;
        card.HiddenTags = source.HiddenTags;
        card.State = source.State;
        var leftSocket = AndroidGameIds.Unboxed(() => source.LeftSocketId);
        card.LeftSocketId = leftSocket.HasValue
            ? new Il2CppSystem.Nullable<EContainerSocketId>(leftSocket.Value)
            : new Il2CppSystem.Nullable<EContainerSocketId>();

        var run = Data.Run?.TryCast<IRun>();
        projection = new AndroidUpgradePreviewValueProjection(source, card, template, run);
        projection.ApplyRuntimeMultipliers(itemTemplate, sourceAttributes, attributes, run);
        card.Attributes = Write(attributes);
        return true;
    }

    /// <summary>The value <paramref name="token"/> resolves to on the next-tier copy.</summary>
    public bool TryResolve(ITooltipToken token, out float value)
    {
        value = default;
        var context = new TooltipContext(_card, _template, _valueContext);
        Func<Il2CppSystem.Nullable<float>>? resolve = null;
        var attribute = token.TryCast<TooltipComponentAttribute>();
        var ability = attribute == null ? token.TryCast<TooltipComponentAbility>() : null;
        var aura =
            attribute == null && ability == null ? token.TryCast<TooltipComponentAura>() : null;
        if (attribute != null)
        {
            var referenced = AndroidGameIds.Unboxed(() => attribute.ReferencedAttribute);
            if (referenced.HasValue)
            {
                var projected = TooltipComponentAttribute.Create(
                    context,
                    referenced.Value.ToString(),
                    attribute.StartWordIndex
                );
                resolve = projected == null ? null : projected.Resolve;
            }
        }
        else if (ability != null)
        {
            var projected = TooltipComponentAbility.Create(
                context,
                ability.EffectId,
                NullableOf(AndroidGameIds.Unboxed(() => ability.Accessor)),
                ability.StartWordIndex
            );
            resolve = projected == null ? null : projected.Resolve;
        }
        else if (aura != null)
        {
            var projected = TooltipComponentAura.Create(
                context,
                aura.EffectId,
                NullableOf(AndroidGameIds.Unboxed(() => aura.Accessor)),
                aura.StartWordIndex
            );
            resolve = projected == null ? null : projected.Resolve;
        }

        var resolved = resolve == null ? null : AndroidGameIds.Unboxed(resolve);
        if (!resolved.HasValue)
            return false;
        value = resolved.Value;
        return true;
    }

    public bool TryResolveEffectiveCooldowns(out float currentSeconds, out float upgradedSeconds) =>
        UpgradePreviewValueMath.TryEffectiveCooldownSeconds(
            Attribute(_source, ECardAttributeType.CooldownMax),
            Attribute(_card, ECardAttributeType.CooldownMax),
            out currentSeconds,
            out upgradedSeconds
        );

    private void ApplyRuntimeMultipliers(
        TCardItem template,
        Dictionary<ECardAttributeType, int> sourceAttributes,
        Dictionary<ECardAttributeType, int> attributes,
        IRun? run
    )
    {
        Dictionary<ECardAttributeType, UpgradePreviewMultipliers> auraMultipliers;
        try
        {
            // The auras read the copy's attributes as they stand after the additive projection.
            _card.Attributes = Write(attributes);
            auraMultipliers = AuraMultipliers.Resolve(_source, _card, run);
        }
        catch (Exception exception)
        {
            MultiplierFailure = exception;
            auraMultipliers = new Dictionary<ECardAttributeType, UpgradePreviewMultipliers>();
        }

        foreach (var attribute in attributes.Keys.ToArray())
        {
            var currentBase = BaseValue(template, attribute, _source.Tier);
            var nextBase = BaseValue(template, attribute, _card.Tier);
            if (!nextBase.HasValue)
                continue;

            var multipliers = UpgradePreviewMultipliers.Identity;
            if (attribute == ECardAttributeType.CooldownMax)
            {
                multipliers = multipliers.Times(
                    new UpgradePreviewMultipliers(
                        CooldownMultiplier(sourceAttributes),
                        CooldownMultiplier(attributes)
                    )
                );
            }
            if (auraMultipliers.TryGetValue(attribute, out var auraMultiplier))
                multipliers = multipliers.Times(auraMultiplier);

            if (
                UpgradePreviewValueMath.TryApplyMultipliers(
                    currentBase.HasValue
                    && sourceAttributes.TryGetValue(attribute, out var currentValue)
                        ? currentValue
                        : null,
                    currentBase,
                    nextBase.Value,
                    multipliers,
                    out var projectedValue
                )
            )
                attributes[attribute] = projectedValue;
        }
    }

    private static float CooldownMultiplier(Dictionary<ECardAttributeType, int> attributes) =>
        UpgradePreviewValueMath.CooldownMultiplier(
            attributes.GetValueOrDefault(ECardAttributeType.PercentCooldownReduction)
        );

    private static int? BaseValue(TCardItem template, ECardAttributeType attribute, ETier tier) =>
        AndroidGameIds.Unboxed(() => template.GetAttributeBaseValueAtTier(attribute, tier));

    private static int? Attribute(Card card, ECardAttributeType attribute)
    {
        var attributes = card.Attributes;
        return attributes != null && attributes.TryGetValue(attribute, out var value)
            ? value
            : null;
    }

    private static Dictionary<ECardAttributeType, int> Read(
        Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int>? attributes
    )
    {
        var values = new Dictionary<ECardAttributeType, int>();
        if (attributes == null)
            return values;
        foreach (var entry in attributes)
            values[entry.Key] = entry.Value;
        return values;
    }

    private static Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int> Write(
        Dictionary<ECardAttributeType, int> attributes
    )
    {
        var native = new Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int>();
        foreach (var entry in attributes)
            native[entry.Key] = entry.Value;
        return native;
    }

    private static Il2CppSystem.Nullable<T> NullableOf<T>(T? value)
        where T : unmanaged =>
        value.HasValue ? new Il2CppSystem.Nullable<T>(value.Value) : new Il2CppSystem.Nullable<T>();

    /// <summary>
    /// The desktop's <c>UpgradePreviewAttributeMultiplierResolver</c>: the out-of-combat
    /// <c>Multiply</c> attribute auras that reach the item, from itself and from every card its
    /// player and opponent hold, once as it is and once with its own auras at the next tier.
    /// </summary>
    private static class AuraMultipliers
    {
        public static Dictionary<ECardAttributeType, UpgradePreviewMultipliers> Resolve(
            ItemCard source,
            ItemCard projected,
            IRun? run
        )
        {
            var multipliers = new Dictionary<ECardAttributeType, UpgradePreviewMultipliers>();
            foreach (var auraSource in Sources(source, run))
            {
                Accumulate(source, auraSource, run, projectedValue: false, multipliers);
                var projectedSource = auraSource.Pointer == source.Pointer ? projected : auraSource;
                Accumulate(projected, projectedSource, run, projectedValue: true, multipliers);
            }
            return multipliers;
        }

        private static void Accumulate(
            ItemCard target,
            Card auraSource,
            IRun? run,
            bool projectedValue,
            Dictionary<ECardAttributeType, UpgradePreviewMultipliers> multipliers
        )
        {
            foreach (var aura in ActiveAuras(auraSource))
            {
                var action = aura.Action?.TryCast<TAuraActionCardModifyAttribute>();
                if (
                    action == null
                    || action.Operation != EAttributeModifierOperation.Multiply
                    || !IsActiveOutOfCombat(auraSource, aura, run)
                    || !Targets(target, auraSource, action, run)
                )
                    continue;

                var value = action.Value.GetValue(
                    new ValueContext(run!, auraSource.TryCast<ICard>())
                );
                var current = multipliers.GetValueOrDefault(
                    action.AttributeType,
                    UpgradePreviewMultipliers.Identity
                );
                multipliers[action.AttributeType] = projectedValue
                    ? current.TimesProjected(value)
                    : current.TimesCurrent(value);
            }
        }

        private static IEnumerable<Card> Sources(ItemCard source, IRun? run)
        {
            yield return source;
            if (run == null)
                yield break;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (source.InstanceId?.Value is { } sourceId)
                seen.Add(sourceId);
            foreach (var player in new[] { run.GetPlayer(), run.GetOpponent() })
            {
                if (player == null)
                    continue;
                var cards = new List<ICard?>();
                foreach (var inventory in new[] { player.Hand, player.Stash, player.Socket })
                {
                    var items = inventory?.GetItems();
                    if (items != null)
                        foreach (var item in items)
                            cards.Add(item);
                }
                var skills = player.TryCast<Player>()?.Skills;
                if (skills != null)
                    foreach (var skill in skills)
                        cards.Add(skill?.TryCast<ICard>());

                foreach (var candidate in cards)
                {
                    var card = candidate?.TryCast<Card>();
                    if (card?.InstanceId?.Value is { } id && seen.Add(id))
                        yield return card;
                }
            }
        }

        private static IEnumerable<TCardAura> ActiveAuras(Card card)
        {
            var template = card.Template;
            var tierData = template?.TryCast<IHasTierData>();
            if (tierData != null)
            {
                var tierAuras = tierData.GetAuraTemplatesByTier(card.Tier);
                if (tierAuras != null)
                    foreach (var aura in tierAuras)
                        if (aura != null)
                            yield return aura;
            }
            else if (template?.Auras != null)
            {
                foreach (var entry in template.Auras)
                    if (entry.Value != null)
                        yield return entry.Value;
            }

            var item = card.TryCast<ItemCard>();
            var itemTemplate = template?.TryCast<TCardItem>();
            if (item == null || itemTemplate == null)
                yield break;

            var enchantment = AndroidGameIds.Unboxed(() => item.Enchantment);
            if (
                enchantment.HasValue
                && itemTemplate.TryGetEnchantmentTemplate(enchantment.Value, out var enchant)
                && enchant?.Auras != null
            )
            {
                foreach (var entry in enchant.Auras)
                    if (entry.Value != null)
                        yield return entry.Value;
            }

            var quests = itemTemplate.Quests;
            if (quests == null)
                yield break;
            var owner = item.TryCast<ICard>();
            foreach (var group in quests)
            {
                if (group?.Entries == null)
                    continue;
                foreach (var entry in group.Entries)
                {
                    var reward = entry != null && entry.IsComplete(owner) ? entry.Reward : null;
                    if (reward?.HasAuras != true || reward.Auras == null)
                        continue;
                    foreach (var aura in reward.Auras)
                        if (aura.Value != null)
                            yield return aura.Value;
                }
            }
        }

        private static bool IsActiveOutOfCombat(Card auraSource, TCardAura aura, IRun? run)
        {
            if ((aura.WorksIn & EEffectWorksIn.OutOfCombatOnly) == 0)
                return false;

            if (auraSource.Type == ECardType.Item)
            {
                var section = AndroidGameIds.Unboxed(() => auraSource.Section);
                var inactive = aura.ActiveIn switch
                {
                    EEffectActiveIn.HandOnly => section != EInventorySection.Hand,
                    EEffectActiveIn.StashOnly => section != EInventorySection.Stash,
                    EEffectActiveIn.HandAndStash => section == null,
                    _ => false,
                };
                if (inactive)
                    return false;
            }

            var prerequisites = aura.Prerequisites;
            if (prerequisites == null)
                return true;
            var context = new PrereqContext(run!, auraSource.TryCast<ICard>());
            foreach (var prerequisite in prerequisites)
                if (prerequisite != null && !prerequisite.IsSatisfiedBy(context))
                    return false;
            return true;
        }

        private static bool Targets(
            ItemCard target,
            Card auraSource,
            TAuraActionCardModifyAttribute action,
            IRun? run
        )
        {
            var targeting = action.Target;
            if (targeting == null)
                return false;
            if (run == null)
                return auraSource.Pointer == target.Pointer
                    && targeting.TryCast<TTargetCardSelf>() != null;

            var targets = targeting.GetTargets(
                new TargetingContext(run, auraSource.TryCast<ICard>()),
                new Il2CppSystem.Nullable<uint>(),
                null,
                null
            );
            if (targets == null)
                return false;
            var targetId = target.InstanceId?.Value;
            foreach (var candidate in targets)
                if (candidate != null && candidate.GetInstanceId()?.Value == targetId)
                    return true;
            return false;
        }
    }
}
