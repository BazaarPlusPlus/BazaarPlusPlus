#nullable enable
namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal readonly record struct RandomizedCollectibleCandidate(
    string? CollectionItemId,
    bool IsDefault
);

internal static class RandomizedCollectibleLoadoutCore
{
    public static void Apply<TCollectionKind>(
        IEnumerable<TCollectionKind> collectionKinds,
        Func<TCollectionKind, IEnumerable<RandomizedCollectibleCandidate>> getAvailableCandidates,
        Func<TCollectionKind, IEnumerable<string>?> loadSelectedIds,
        Action<TCollectionKind, IReadOnlyCollection<string>> saveSelectedIds,
        Func<int, int> selectIndex,
        Action<TCollectionKind, string?> applySelection,
        Action<TCollectionKind, Exception> reportFailure
    )
    {
        if (collectionKinds == null)
            throw new ArgumentNullException(nameof(collectionKinds));
        if (getAvailableCandidates == null)
            throw new ArgumentNullException(nameof(getAvailableCandidates));
        if (loadSelectedIds == null)
            throw new ArgumentNullException(nameof(loadSelectedIds));
        if (saveSelectedIds == null)
            throw new ArgumentNullException(nameof(saveSelectedIds));
        if (selectIndex == null)
            throw new ArgumentNullException(nameof(selectIndex));
        if (applySelection == null)
            throw new ArgumentNullException(nameof(applySelection));
        if (reportFailure == null)
            throw new ArgumentNullException(nameof(reportFailure));

        foreach (var collectionKind in collectionKinds)
        {
            try
            {
                var availableCandidates = (
                    getAvailableCandidates(collectionKind)
                    ?? Array.Empty<RandomizedCollectibleCandidate>()
                )
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.CollectionItemId))
                    .ToArray();
                if (availableCandidates.Length == 0)
                    continue;

                var state = RandomHeroSkinPoolStateFactory.Create(
                    availableCandidates.Select(candidate => candidate.CollectionItemId!),
                    loadSelectedIds(collectionKind)
                );
                saveSelectedIds(collectionKind, state.SelectedSkinIds);

                var selectedCandidates = availableCandidates
                    .Where(candidate => state.IsSelected(candidate.CollectionItemId))
                    .ToArray();
                if (selectedCandidates.Length == 0)
                    continue;

                var selectedIndex = selectIndex(selectedCandidates.Length);
                if (selectedIndex < 0 || selectedIndex >= selectedCandidates.Length)
                {
                    throw new InvalidOperationException(
                        "Random collectible index was out of range."
                    );
                }

                var selectedCandidate = selectedCandidates[selectedIndex];
                applySelection(
                    collectionKind,
                    selectedCandidate.IsDefault ? null : selectedCandidate.CollectionItemId
                );
            }
            catch (Exception ex)
            {
                reportFailure(collectionKind, ex);
            }
        }
    }
}
