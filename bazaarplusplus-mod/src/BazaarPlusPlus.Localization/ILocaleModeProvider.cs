#nullable enable

namespace BazaarPlusPlus.Localization;

internal interface ILocaleModeProvider
{
    BppChineseLocaleMode CurrentMode { get; }
}
