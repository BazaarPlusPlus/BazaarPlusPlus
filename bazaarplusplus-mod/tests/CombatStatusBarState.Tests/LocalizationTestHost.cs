#nullable enable
using BazaarPlusPlus.Localization;
using Xunit;

// L.Install mutates process-wide localization state, so tests need serial execution.
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
