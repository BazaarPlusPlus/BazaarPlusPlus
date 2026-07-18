#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.TagTypography;

internal readonly struct ReferenceTagBase
{
    private ReferenceTagBase(EHiddenTag? hiddenTag, ECardTag? cardTag)
    {
        HiddenTag = hiddenTag;
        CardTag = cardTag;
    }

    public EHiddenTag? HiddenTag { get; }

    public ECardTag? CardTag { get; }

    public bool HasValue => HiddenTag.HasValue || CardTag.HasValue;

    public static ReferenceTagBase ForHiddenTag(EHiddenTag tag) => new(tag, null);

    public static ReferenceTagBase ForCardTag(ECardTag tag) => new(null, tag);
}

internal static class ReferenceTagBaseResolver
{
    public static bool TryResolve(EHiddenTag tag, out ReferenceTagBase baseTag)
    {
        baseTag = tag switch
        {
            EHiddenTag.DamageReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Damage),
            EHiddenTag.HealReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Heal),
            EHiddenTag.BurnReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Burn),
            EHiddenTag.PoisonReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Poison),
            EHiddenTag.JoyReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Joy),
            EHiddenTag.ShieldReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Shield),
            EHiddenTag.RegenReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Regen),
            EHiddenTag.HealthReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Health),
            EHiddenTag.FreezeReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Freeze),
            EHiddenTag.HasteReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Haste),
            EHiddenTag.SlowReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Slow),
            EHiddenTag.EconomyReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Income),
            EHiddenTag.CooldownReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Cooldown),
            EHiddenTag.AmmoReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Ammo),
            EHiddenTag.CritReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Crit),
            EHiddenTag.QuestReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Quest),
            EHiddenTag.FlyingReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Flying),
            EHiddenTag.RageReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Rage),
            EHiddenTag.HeatedReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Heated),
            EHiddenTag.ChilledReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Chilled),
            EHiddenTag.TempoReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Tempo),
            EHiddenTag.PotionReference => ReferenceTagBase.ForCardTag(ECardTag.Potion),
            _ => default,
        };
        return baseTag.HasValue;
    }
}
