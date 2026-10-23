#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Player-facing CardHiddenTag keyword options, ordered to mirror BazaarDB's Types & Tags
// mechanism slice while excluding system-only hidden tags. Related keywords stay in this same
// keyword facet because they are still card HiddenTags; the UI renders them as a later subsection.
// Catalog availability then removes options that current game data does not actually use.
public static class CollectionKeywordWhitelist
{
    public static readonly IReadOnlyList<CardHiddenTag> Ordered = new[]
    {
        CardHiddenTag.Quest,
        CardHiddenTag.Flying,
        CardHiddenTag.Haste,
        CardHiddenTag.Charge,
        CardHiddenTag.Cooldown,
        CardHiddenTag.Slow,
        CardHiddenTag.Freeze,
        CardHiddenTag.Damage,
        CardHiddenTag.Shield,
        CardHiddenTag.Heal,
        CardHiddenTag.Health,
        CardHiddenTag.Burn,
        CardHiddenTag.Poison,
        CardHiddenTag.Regen,
        CardHiddenTag.Crit,
        CardHiddenTag.Ammo,
        CardHiddenTag.Lifesteal,
        CardHiddenTag.Rage,
        CardHiddenTag.Gold,
        CardHiddenTag.Income,
        CardHiddenTag.Value,
        CardHiddenTag.Tempo,
        CardHiddenTag.QuestReference,
        CardHiddenTag.FlyingReference,
        CardHiddenTag.HasteReference,
        CardHiddenTag.CooldownReference,
        CardHiddenTag.SlowReference,
        CardHiddenTag.FreezeReference,
        CardHiddenTag.DamageReference,
        CardHiddenTag.ShieldReference,
        CardHiddenTag.HealReference,
        CardHiddenTag.HealthReference,
        CardHiddenTag.BurnReference,
        CardHiddenTag.PoisonReference,
        CardHiddenTag.RegenReference,
        CardHiddenTag.CritReference,
        CardHiddenTag.AmmoReference,
        CardHiddenTag.RageReference,
        CardHiddenTag.TempoReference,
        CardHiddenTag.EconomyReference,
        CardHiddenTag.PotionReference,
    };

    public static bool IsRelatedKeyword(CardHiddenTag tag) => IsReferenceKeyword(tag);

    public static bool IsReferenceKeyword(CardHiddenTag tag) =>
        tag
            is CardHiddenTag.QuestReference
                or CardHiddenTag.FlyingReference
                or CardHiddenTag.HasteReference
                or CardHiddenTag.CooldownReference
                or CardHiddenTag.SlowReference
                or CardHiddenTag.FreezeReference
                or CardHiddenTag.DamageReference
                or CardHiddenTag.ShieldReference
                or CardHiddenTag.HealReference
                or CardHiddenTag.HealthReference
                or CardHiddenTag.BurnReference
                or CardHiddenTag.PoisonReference
                or CardHiddenTag.RegenReference
                or CardHiddenTag.CritReference
                or CardHiddenTag.AmmoReference
                or CardHiddenTag.RageReference
                or CardHiddenTag.TempoReference
                or CardHiddenTag.EconomyReference
                or CardHiddenTag.PotionReference;
}
