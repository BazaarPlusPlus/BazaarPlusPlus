#nullable enable
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.GameInterop.Cards;

internal static class CardDisplayNameResolver
{
    internal static string Resolve(TCardBase? template)
    {
        if (template is null)
            return string.Empty;

        var title = template.Localization?.Title;
        if (title is not null)
        {
            try
            {
                var localized = TheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(title);
                if (!string.IsNullOrWhiteSpace(localized))
                    return localized;
            }
            catch (Exception)
            {
                // Localization can be unavailable during early startup; use authored metadata.
            }

            if (!string.IsNullOrWhiteSpace(title.Text))
                return title.Text;
            if (!string.IsNullOrWhiteSpace(title.Key))
                return title.Key;
        }

        if (!string.IsNullOrWhiteSpace(template.InternalName))
            return template.InternalName;
        return template.Id == Guid.Empty ? template.GetType().Name : template.Id.ToString("D");
    }
}
