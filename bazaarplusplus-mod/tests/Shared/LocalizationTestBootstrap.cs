#nullable enable
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Tests;

// Install a neutral source before the entry point because production localization calls throw until L is installed.
internal static class LocalizationTestBootstrap
{
    [ModuleInitializer]
    internal static void Install() =>
        L.Install(() => string.Empty, () => BppChineseLocaleMode.Mainland);
}
