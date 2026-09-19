#nullable enable
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Tests;

// These exe-runners drive production code that reads the localization facade L, which throws
// until installed. Install a neutral English/Mainland source before the entry point runs so
// the code under test takes the same default path it did before the localization extraction.
internal static class LocalizationTestBootstrap
{
    [ModuleInitializer]
    internal static void Install() =>
        L.Install(new EnglishLanguageProvider(), new MainlandLocaleModeProvider());

    private sealed class EnglishLanguageProvider : ILanguageProvider
    {
        public string CurrentLanguageCode => string.Empty;
    }

    private sealed class MainlandLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
