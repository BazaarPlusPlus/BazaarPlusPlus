#nullable enable
namespace BazaarPlusPlus.GameInterop.Fonts;

internal static class NativeGameFontSelection
{
    internal const float FallbackRetrySeconds = 30f;

    internal const float NoFallbackRetryWait = 0f;

    // Throttle per-frame or per-tooltip polling because each attempt blocks the main thread
    // on every failed key.
    internal static bool IsFallbackRetryWaiting(float now, float retryAt) => now < retryAt;

    internal static float NextFallbackRetryAt(
        float now,
        bool attempted,
        bool loadedAny,
        float retryAt
    ) =>
        loadedAny ? NoFallbackRetryWait
        : attempted ? now + FallbackRetrySeconds
        : retryAt;

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
