#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.Trigger;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactCriticalCapability
{
    internal static IReadOnlyCollection<string>? ReadCritCapableEffectIds(
        ECardType cardType,
        IEnumerable<TCardAbility>? abilities,
        IReadOnlyCollection<EHiddenTag>? hiddenTags
    )
    {
        if (abilities == null)
            return null;

        var cardHasCritOverride = hiddenTags?.Contains(EHiddenTag.CanCrit) == true;
        if (!cardHasCritOverride && cardType != ECardType.Item)
            return null;

        var effectIds = abilities
            .Where(ability =>
                ability != null
                && !string.IsNullOrWhiteSpace(ability.Id)
                && IsCritEffect(ability.Action)
                && (cardHasCritOverride || ability.Trigger is TTriggerOnCardFired)
            )
            .Select(ability => ability.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return effectIds.Length == 0 ? null : effectIds;
    }

    private static bool IsCritEffect(ITAction? action) =>
        action
            is TActionPlayerDamage
                or TActionPlayerShieldApply
                or TActionPlayerHeal
                or TActionPlayerReviveHeal
                or TActionPlayerBurnApply
                or TActionPlayerPoisonApply
                or TActionPlayerRegenApply;
}
