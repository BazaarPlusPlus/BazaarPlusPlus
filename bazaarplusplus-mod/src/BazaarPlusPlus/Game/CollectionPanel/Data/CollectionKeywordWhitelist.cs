#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Player-facing EHiddenTag keyword options, ordered to mirror BazaarDB's Types & Tags
// mechanism slice while excluding system-only hidden tags. Reference tags stay in this same
// keyword facet because they are still card HiddenTags; the UI renders them as a later subsection.
// Catalog availability then removes options that current game data does not actually use.
internal static class CollectionKeywordWhitelist
{
    public static readonly IReadOnlyList<EHiddenTag> Ordered = new[]
    {
        EHiddenTag.Quest,
        EHiddenTag.Flying,
        EHiddenTag.Haste,
        EHiddenTag.Charge,
        EHiddenTag.Cooldown,
        EHiddenTag.Slow,
        EHiddenTag.Freeze,
        EHiddenTag.Damage,
        EHiddenTag.Shield,
        EHiddenTag.Heal,
        EHiddenTag.Health,
        EHiddenTag.Burn,
        EHiddenTag.Poison,
        EHiddenTag.Regen,
        EHiddenTag.Crit,
        EHiddenTag.Ammo,
        EHiddenTag.Lifesteal,
        EHiddenTag.Rage,
        EHiddenTag.Gold,
        EHiddenTag.Income,
        EHiddenTag.Value,
        EHiddenTag.QuestReference,
        EHiddenTag.FlyingReference,
        EHiddenTag.HasteReference,
        EHiddenTag.CooldownReference,
        EHiddenTag.SlowReference,
        EHiddenTag.FreezeReference,
        EHiddenTag.DamageReference,
        EHiddenTag.ShieldReference,
        EHiddenTag.HealReference,
        EHiddenTag.HealthReference,
        EHiddenTag.BurnReference,
        EHiddenTag.PoisonReference,
        EHiddenTag.RegenReference,
        EHiddenTag.CritReference,
        EHiddenTag.AmmoReference,
        EHiddenTag.RageReference,
        EHiddenTag.EconomyReference,
    };

    public static bool IsReferenceKeyword(EHiddenTag tag) =>
        tag is
            EHiddenTag.QuestReference
            or EHiddenTag.FlyingReference
            or EHiddenTag.HasteReference
            or EHiddenTag.CooldownReference
            or EHiddenTag.SlowReference
            or EHiddenTag.FreezeReference
            or EHiddenTag.DamageReference
            or EHiddenTag.ShieldReference
            or EHiddenTag.HealReference
            or EHiddenTag.HealthReference
            or EHiddenTag.BurnReference
            or EHiddenTag.PoisonReference
            or EHiddenTag.RegenReference
            or EHiddenTag.CritReference
            or EHiddenTag.AmmoReference
            or EHiddenTag.RageReference
            or EHiddenTag.EconomyReference;
}
