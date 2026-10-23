using BazaarPlusPlus.Core.Cards;
using Il2CppBazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android answer to <c>BuildRecommendationRepository</c>'s template-size port, as the
/// desktop's <c>StaticDataCardSizeSource</c>: the game's static template size as a facet, or null
/// when static data is not ready or the lookup fails. Main thread only: the repository asks while
/// it projects a recommendation, which the Live Build panel does on its tick.
/// </summary>
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
