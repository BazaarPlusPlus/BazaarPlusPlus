#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Marker MonoBehaviour attached to every card instance the collection grid instantiates.
// Harmony patches use it to keep collection-owned native cards separate from HistoryPanel,
// live shop/board previews, and other game-owned CardPreviewBase instances.
internal sealed class CollectionPanelOwnedMarker : MonoBehaviour
{
    public Material? SharedPreviewMaterial;
}
