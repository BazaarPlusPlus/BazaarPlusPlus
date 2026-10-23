#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

[Flags]
public enum CollectionMechanic
{
    None = 0,
    Multicast = 1 << 0,
    Destroy = 1 << 1,
}

public static class CollectionMechanics
{
    public static readonly IReadOnlyList<CollectionMechanic> Ordered = new[]
    {
        CollectionMechanic.Multicast,
        CollectionMechanic.Destroy,
    };

    public static bool Has(this CollectionMechanic facts, CollectionMechanic mechanic) =>
        mechanic != CollectionMechanic.None && (facts & mechanic) == mechanic;

    public static bool TryFromHiddenTag(CardHiddenTag hiddenTag, out CollectionMechanic mechanic)
    {
        // AbsorbDestroy is not on the keyword whitelist, so mapping it here only feeds the
        // Destroy mechanic fact — it never surfaces as its own keyword chip.
        mechanic = hiddenTag switch
        {
            CardHiddenTag.Multicast => CollectionMechanic.Multicast,
            CardHiddenTag.AbsorbDestroy => CollectionMechanic.Destroy,
            _ => CollectionMechanic.None,
        };
        return mechanic != CollectionMechanic.None;
    }
}
