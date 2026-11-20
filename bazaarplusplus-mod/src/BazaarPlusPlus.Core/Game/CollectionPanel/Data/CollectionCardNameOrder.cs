#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Rank names once per culture to avoid repeated culture-sensitive comparisons. Equal names share a rank to preserve the original sort.
internal sealed class CollectionCardNameOrder
{
    private readonly IReadOnlyList<CollectionCardVm> _cards;

    private CollectionCardNameOrder(IReadOnlyList<CollectionCardVm> cards, CultureInfo culture)
    {
        _cards = cards;
        Culture = culture;
    }

    public CultureInfo Culture { get; }

    public static int CompareNames(CollectionCardVm a, CollectionCardVm b) =>
        string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);

    public static void Assign(IReadOnlyList<CollectionCardVm> cards)
    {
        if (cards.Count == 0)
            return;

        var culture = CultureInfo.CurrentCulture;
        var current = cards[0].NameOrder;
        if (
            current != null
            && ReferenceEquals(current._cards, cards)
            && current.Culture.Equals(culture)
        )
            return;

        var order = new CollectionCardNameOrder(cards, culture);
        var sorted = new CollectionCardVm[cards.Count];
        for (var index = 0; index < sorted.Length; index++)
            sorted[index] = cards[index];
        Array.Sort(sorted, CompareNames);

        var rank = 0;
        for (var index = 0; index < sorted.Length; index++)
        {
            if (index > 0 && CompareNames(sorted[index - 1], sorted[index]) != 0)
                rank = index;
            sorted[index].NameRank = rank;
            sorted[index].NameOrder = order;
        }
    }

    public static bool Covers(IReadOnlyList<CollectionCardVm> cards)
    {
        if (cards.Count == 0)
            return false;

        var order = cards[0].NameOrder;
        if (order == null || !order.Culture.Equals(CultureInfo.CurrentCulture))
            return false;
        foreach (var card in cards)
        {
            if (!ReferenceEquals(card.NameOrder, order))
                return false;
        }
        return true;
    }
}
