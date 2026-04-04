using BazaarPlusPlus.Core.Runtime;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.NameOverride;

internal static class NameOverrideUiRefresh
{
    internal static void TryRefreshVisibleHeroBanners()
    {
        var displayName = BppClientCacheBridge.TryGetProfileDisplayUsername();
        if (string.IsNullOrWhiteSpace(displayName))
            return;

        var banners = Object.FindObjectsOfType<HeroBannerController>();
        if (banners == null || banners.Length == 0)
            return;

        foreach (var banner in banners)
        {
            if (banner == null || !banner.isActiveAndEnabled)
                continue;

            banner.SetHeroName(displayName, 0);
        }
    }
}
