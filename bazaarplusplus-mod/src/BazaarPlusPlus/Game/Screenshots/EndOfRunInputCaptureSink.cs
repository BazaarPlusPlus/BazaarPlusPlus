#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunInputCaptureSink
    : MonoBehaviour,
        IPointerClickHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler,
        IScrollHandler,
        IInitializePotentialDragHandler,
        ISubmitHandler,
        ICancelHandler,
        IMoveHandler,
        ISelectHandler,
        IDeselectHandler,
        IUpdateSelectedHandler
{
    public void CaptureFocus()
    {
        var eventSystem = EventSystem.current;
        if (
            eventSystem == null
            || ReferenceEquals(eventSystem.currentSelectedGameObject, gameObject)
        )
            return;

        eventSystem.SetSelectedGameObject(gameObject);
    }

    public void ReleaseFocus()
    {
        var eventSystem = EventSystem.current;
        if (
            eventSystem == null
            || !ReferenceEquals(eventSystem.currentSelectedGameObject, gameObject)
        )
            return;

        eventSystem.SetSelectedGameObject(null);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        CaptureFocus();
        EndOfRunScreenshotController.NotifyBlockerClick();
        eventData.Use();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        CaptureFocus();
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        CaptureFocus();
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnDrop(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnScroll(PointerEventData eventData)
    {
        CaptureFocus();
        eventData.Use();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.Use();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        eventData.Use();
    }

    public void OnCancel(BaseEventData eventData)
    {
        eventData.Use();
    }

    public void OnMove(AxisEventData eventData)
    {
        eventData.Use();
    }

    public void OnSelect(BaseEventData eventData)
    {
        eventData.Use();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        eventData.Use();
    }

    public void OnUpdateSelected(BaseEventData eventData)
    {
        eventData.Use();
        CaptureFocus();
    }
}
