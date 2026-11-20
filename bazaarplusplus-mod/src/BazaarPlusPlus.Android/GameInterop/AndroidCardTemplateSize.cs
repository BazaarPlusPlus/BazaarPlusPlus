using BazaarPlusPlus.Core.Cards;
using Il2CppBazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.Android.GameInterop;

internal static class AndroidCardTemplateSize
{
    public static CardSize? Resolve(Guid templateId)
    {
        try
        {
            if (!Il2CppTheBazaar.Data.IsManagerCreated())
                return null;
            var template = Il2CppTheBazaar
                .Data.GetStatic()
                ?.GetCardById(Il2CppSystem.Guid.Parse(templateId.ToString()))
                ?.TryCast<TCardBase>();
            return
                template != null && AndroidCardFacets.Size.TryToFacet(template.Size, out var size)
                ? size
                : null;
        }
        catch
        {
            return null;
        }
    }
}
