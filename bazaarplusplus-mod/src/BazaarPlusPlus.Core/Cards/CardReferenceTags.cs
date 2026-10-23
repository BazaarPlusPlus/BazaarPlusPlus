#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>The tag a reference hidden tag points at: a hidden tag, or a public card tag.</summary>
public readonly struct CardReferenceBase
{
    private CardReferenceBase(CardHiddenTag? hiddenTag, CardTag? cardTag)
    {
        HiddenTag = hiddenTag;
        CardTag = cardTag;
    }

    public CardHiddenTag? HiddenTag { get; }

    public CardTag? CardTag { get; }

    public bool HasValue => HiddenTag.HasValue || CardTag.HasValue;

    public static CardReferenceBase ForHiddenTag(CardHiddenTag tag) => new(tag, null);

    public static CardReferenceBase ForCardTag(CardTag tag) => new(null, tag);
}

public static class CardReferenceTags
{
    public static bool TryResolveBase(CardHiddenTag tag, out CardReferenceBase baseTag)
    {
        baseTag = tag switch
        {
            CardHiddenTag.DamageReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Damage),
            CardHiddenTag.HealReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Heal),
            CardHiddenTag.BurnReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Burn),
            CardHiddenTag.PoisonReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Poison),
            CardHiddenTag.JoyReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Joy),
            CardHiddenTag.ShieldReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Shield),
            CardHiddenTag.RegenReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Regen),
            CardHiddenTag.HealthReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Health),
            CardHiddenTag.FreezeReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Freeze),
            CardHiddenTag.HasteReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Haste),
            CardHiddenTag.SlowReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Slow),
            CardHiddenTag.EconomyReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Income),
            CardHiddenTag.CooldownReference => CardReferenceBase.ForHiddenTag(
                CardHiddenTag.Cooldown
            ),
            CardHiddenTag.AmmoReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Ammo),
            CardHiddenTag.CritReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Crit),
            CardHiddenTag.QuestReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Quest),
            CardHiddenTag.FlyingReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Flying),
            CardHiddenTag.RageReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Rage),
            CardHiddenTag.HeatedReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Heated),
            CardHiddenTag.ChilledReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Chilled),
            CardHiddenTag.TempoReference => CardReferenceBase.ForHiddenTag(CardHiddenTag.Tempo),
            CardHiddenTag.PotionReference => CardReferenceBase.ForCardTag(CardTag.Potion),
            _ => default,
        };
        return baseTag.HasValue;
    }
}
