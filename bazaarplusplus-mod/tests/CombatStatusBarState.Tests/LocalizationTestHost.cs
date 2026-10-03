#nullable enable
using BazaarPlusPlus.Localization;
using Xunit;

// L.Install is process-global mutable state. Tests that resolve localized text mutate it,
// so the whole assembly must run serially to keep one class's install from leaking into
// another that reads L in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BazaarPlusPlus.Tests;

internal static class LocalizationTestHost
{
    internal static void Install(
        string languageCode,
        BppChineseLocaleMode mode = BppChineseLocaleMode.Mainland
    )
    {
        L.Install(() => languageCode, () => mode);
    }
}
