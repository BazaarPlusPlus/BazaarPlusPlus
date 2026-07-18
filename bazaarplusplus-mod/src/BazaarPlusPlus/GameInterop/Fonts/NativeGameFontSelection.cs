#nullable enable
namespace BazaarPlusPlus.GameInterop.Fonts;

internal static class NativeGameFontSelection
{
    internal static bool HasCompleteChain(int expectedCount, int loadedCount) =>
        expectedCount > 0 && loadedCount == expectedCount;

    internal static int FindLastIndexWithSource<T>(
        IReadOnlyList<T> candidates,
        Func<T, bool> hasSource
    )
    {
        if (candidates == null)
            throw new ArgumentNullException(nameof(candidates));
        if (hasSource == null)
            throw new ArgumentNullException(nameof(hasSource));

        for (var index = candidates.Count - 1; index >= 0; index--)
            if (hasSource(candidates[index]))
                return index;

        return -1;
    }
}
