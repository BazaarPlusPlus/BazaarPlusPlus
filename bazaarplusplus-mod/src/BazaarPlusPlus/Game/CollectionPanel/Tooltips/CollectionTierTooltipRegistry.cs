#nullable enable
using System.Runtime.CompilerServices;
using BazaarGameClient.Domain.Models.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Tooltips;

internal static class CollectionTierTooltipRegistry
{
    private static readonly object Gate = new();
    private static readonly HashSet<Card> Cards = new(ReferenceCardComparer.Instance);

    public static void Register(Card? card)
    {
        if (card == null)
            return;
        lock (Gate)
            Cards.Add(card);
    }

    public static void Unregister(Card? card)
    {
        if (card == null)
            return;
        lock (Gate)
            Cards.Remove(card);
    }

    public static bool Contains(Card? card)
    {
        if (card == null)
            return false;
        lock (Gate)
            return Cards.Contains(card);
    }

    private sealed class ReferenceCardComparer : IEqualityComparer<Card>
    {
        public static readonly ReferenceCardComparer Instance = new();

        public bool Equals(Card? x, Card? y) => ReferenceEquals(x, y);

        public int GetHashCode(Card value) => RuntimeHelpers.GetHashCode(value);
    }
}
