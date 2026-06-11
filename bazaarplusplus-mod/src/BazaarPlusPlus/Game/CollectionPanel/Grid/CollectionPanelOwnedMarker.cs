#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Marker MonoBehaviour attached to every card instance the collection pool instantiates.
// Used by Harmony patches to gate the cache + material-sharing replacements so the patches
// only affect cards owned by this panel — HistoryPanel and live shop/board cards run the
// original game code untouched.
//
// CurrentArtKey records the artKey the card's _cardMaterial currently points at. The
// LoadArt patch updates it on every (re)bind so we know which L2 entry to Release when the
// card binds to a new artKey or is destroyed.
internal sealed class CollectionPanelOwnedMarker : MonoBehaviour
{
    public string? CurrentArtKey;
}
