#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Mobile.Collection;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

/// <summary>
/// The Unity callbacks the touch Collection needs on the Mono host: it warms the catalog each
/// frame the panel is closed, as the desktop panel does, so the first tap opens on it, and writes a
/// staged hero choice when the app leaves the foreground. The panel itself is the Mobile UI's
/// (<see cref="TouchCollectionEntry"/>).
/// </summary>
internal sealed class CollectionTouchLifecycle : MonoBehaviour
{
    private TouchCollectionEntry? _entry;
    private DesktopCollectionCardCatalog? _catalog;

    public void Initialize(TouchCollectionEntry entry, DesktopCollectionCardCatalog catalog)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    private void Update()
    {
        if (_entry is { IsVisible: false })
            _catalog?.Warm();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            _entry?.FlushHeroPreference();
    }
}
