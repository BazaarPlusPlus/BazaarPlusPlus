#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal static class StaticDataCardSizeSource
{
    public static CardSize? Resolve(Guid templateId)
    {
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, templateId);
            return
                template != null && DesktopCardFacets.Size.TryToFacet(template.Size, out var size)
                ? size
                : null;
        }
        catch
        {
            return null;
        }
    }
}
