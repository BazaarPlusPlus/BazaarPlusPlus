#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal sealed class CollectionCardHoverRelay
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
{
    private INativeCardPreviewSession? _session;

    public void Bind(INativeCardPreviewSession session) => _session = session;

    public void Clear()
    {
        TryInvokeHoverOut();
        _session = null;
    }

    public void OnPointerEnter(PointerEventData _) => _session?.HoverEnter();

    public void OnPointerExit(PointerEventData _) => _session?.HoverExit();

    public void TryInvokeHoverOut() => _session?.HoverExit();
}
