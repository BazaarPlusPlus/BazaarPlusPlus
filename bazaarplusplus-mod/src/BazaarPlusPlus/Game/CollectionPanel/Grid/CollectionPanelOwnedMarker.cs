#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Harmony scopes cache patches to marked cards. The original cache owner prevents delayed destruction from releasing into a newer panel runtime.
// Native OnDestroy must still destroy a material it created.
internal sealed class CollectionPanelOwnedMarker : MonoBehaviour
{
    public CollectionNativeCardPreviewOwner? PreviewOwner;
    public CollectionCardCacheSession? CacheOwner;
    public string? CurrentArtKey;
    public bool CardMaterialOwnedByCache;
    public bool TooltipRegistered;
    public Action? OnArtLoaded;

    public void ReleaseCurrentArtKey()
    {
        if (CacheOwner != null && !string.IsNullOrEmpty(CurrentArtKey))
        {
            CacheOwner.ArtCache.Release(CurrentArtKey!);
            CacheOwner.MaterialCache.Release(CurrentArtKey!);
        }

        CurrentArtKey = null;
        CacheOwner = null;
    }
}
