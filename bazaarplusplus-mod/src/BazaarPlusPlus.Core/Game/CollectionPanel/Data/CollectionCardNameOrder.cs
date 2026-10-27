#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// A catalog's display-name order under the current culture, ranked once when the catalog is
// accepted so the filter engine's sort compares integers instead of running a culture-sensitive
// string comparison per pair. Cards whose names the culture compares equal share a rank, so the
// ranked order is exactly the one CompareNames gives.
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

    // Ranks the catalog's cards; a catalog already ranked under the current culture is kept.
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

    // True when every card carries the same order, ranked under the current culture; otherwise
    // the caller compares names directly.
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
