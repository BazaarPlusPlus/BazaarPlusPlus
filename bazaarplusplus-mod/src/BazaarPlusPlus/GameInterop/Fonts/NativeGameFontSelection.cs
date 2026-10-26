#nullable enable
namespace BazaarPlusPlus.GameInterop.Fonts;

internal static class NativeGameFontSelection
{
    /// <summary>How long a chain that loaded nothing waits before the next load attempt.</summary>
    internal const float FallbackRetrySeconds = 30f;

    /// <summary>The retry time of a chain that has no wait pending.</summary>
    internal const float NoFallbackRetryWait = 0f;

    /// <summary>
    /// A chain inside its wait is not loaded: callers poll per frame or per tooltip, and each
    /// attempt blocks the main thread on every key that failed.
    /// </summary>
    internal static bool IsFallbackRetryWaiting(float now, float retryAt) => now < retryAt;

    /// <summary>
    /// Only an attempt that ran and loaded nothing starts a wait. A skipped call (not ready, or
    /// still waiting) keeps the current wait; a loaded chain clears it.
    /// </summary>
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
