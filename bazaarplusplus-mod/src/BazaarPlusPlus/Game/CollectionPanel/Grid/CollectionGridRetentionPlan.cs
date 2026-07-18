#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal static class CollectionGridRetentionPlan
{
    public static IReadOnlyDictionary<int, int> Build(
        IReadOnlyDictionary<int, Guid> realizedCardIdsByIndex,
        IReadOnlyList<Guid> nextVisibleCardIds
    )
    {
        var nextIndexByCardId = new Dictionary<Guid, int>(nextVisibleCardIds.Count);
        for (var index = 0; index < nextVisibleCardIds.Count; index++)
            nextIndexByCardId.TryAdd(nextVisibleCardIds[index], index);

        var retainedIndices = new Dictionary<int, int>(realizedCardIdsByIndex.Count);
        var claimedNextIndices = new HashSet<int>();
        foreach (var pair in realizedCardIdsByIndex)
        {
            if (
                nextIndexByCardId.TryGetValue(pair.Value, out var nextIndex)
                && claimedNextIndices.Add(nextIndex)
            )
            {
                retainedIndices[pair.Key] = nextIndex;
            }
        }

        return retainedIndices;
    }
}
