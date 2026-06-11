#nullable enable

namespace BazaarPlusPlus.Localization;

internal interface ILanguageProvider
{
    string CurrentLanguageCode { get; }
}
