using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppTheBazaar.Tooltips;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>Merchant and trainer names from the encounter template's localized title.</summary>
internal static class AndroidEncounterNames
{
    public static string Display(Guid templateId, string fallback)
    {
        try
        {
            if (!Il2CppTheBazaar.Data.IsManagerCreated())
                return fallback;
            var title = Il2CppTheBazaar
                .Data.GetStatic()
                ?.GetCardById(Il2CppSystem.Guid.Parse(templateId.ToString()))
                ?.TryCast<TCardBase>()
                ?.Localization?.Title;
            if (title != null)
            {
                var name = TooltipExtensions.GetLocalizedText(title);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception)
        {
            // Static data and localization can be unavailable while the game loads.
        }
        return fallback;
    }
}
