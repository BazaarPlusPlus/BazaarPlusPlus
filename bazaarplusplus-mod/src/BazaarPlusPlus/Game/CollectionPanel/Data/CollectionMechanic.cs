#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

[Flags]
internal enum CollectionMechanic
{
    None = 0,
    Multicast = 1 << 0,
    Destroy = 1 << 1,
}

internal static class CollectionMechanics
{
    public static readonly IReadOnlyList<CollectionMechanic> Ordered = new[]
    {
        CollectionMechanic.Multicast,
        CollectionMechanic.Destroy,
    };

    public static bool Has(this CollectionMechanic facts, CollectionMechanic mechanic) =>
        mechanic != CollectionMechanic.None && (facts & mechanic) == mechanic;
}
