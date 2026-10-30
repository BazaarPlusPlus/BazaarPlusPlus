#nullable enable
using System.Collections;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.GameInterop.Touch;

/// <summary>
/// The game's mobile card press on a mobile platform (ADR-0024). The Mac build keeps the mobile
/// press fields and <c>MainPointerUp</c>, but compiles out the bodies that drive them:
/// <c>CardController.OnPointerDown</c>, <c>OnPointerUp</c> and <c>HoldToLockRoutine</c> are empty.
/// This puts them back from what the build still holds: a press records its position and time;
/// a press held for <c>holdToLockSeconds</c> without moving opens the native lock-mode inspect
/// (tier and enchantment pickers); a release goes through <c>MainPointerUp</c>, which adds the
/// swipe-up buy. The desktop's <c>OnPointerClick</c> would run the same click a second time, so
/// the patch skips it on a mobile platform.
/// </summary>
internal static class TouchCardPress
{
    private static long _holdLocks;

    public static void Down(CardController card, PointerEventData eventData)
    {
        card._pressPos = eventData.position;
        card._pressTime = Time.unscaledTime;
        card._movedSincePress = false;
        card._holdLockTriggered = false;
        StopHold(card);
        if (card.isActiveAndEnabled)
            card._holdLockRoutine = card.StartCoroutine(Hold(card));
    }

    public static void Up(CardController card, PointerEventData eventData)
    {
        StopHold(card);
        card.MainPointerUp(eventData);
    }

    /// <summary>A drag began: the press is a move, not a hold or a tap.</summary>
    public static void Moved(CardController card)
    {
        card._movedSincePress = true;
        StopHold(card);
    }

    private static void StopHold(CardController card)
    {
        if (card._holdLockRoutine == null)
            return;
        card.StopCoroutine(card._holdLockRoutine);
        card._holdLockRoutine = null;
    }

    private static IEnumerator Hold(CardController card)
    {
        var threshold = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10;
        while (Time.unscaledTime - card._pressTime < card.holdToLockSeconds)
        {
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.isPressed)
                yield break;
            if (
                (pointer.position.ReadValue() - card._pressPos).sqrMagnitude
                > threshold * threshold
            )
            {
                card._movedSincePress = true;
                yield break;
            }
            yield return null;
        }
        card._holdLockRoutine = null;
        card._holdLockTriggered = true;
        var tooltips = TheBazaar.Data.TooltipParentComponent;
        var controller = tooltips?.GetCardTooltipController(card.CardData);
        if (controller == null || controller.CurrentCard == null)
        {
            card.Select();
            controller = tooltips?.GetCardTooltipController(card.CardData);
        }
        if (controller == null || controller.IsLocked)
            yield break;
        controller.LockTooltipToggle();
        _holdLocks++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.card_press.hold_locked"),
            ("locked", controller.IsLocked),
            ("hold_locks", _holdLocks)
        );
    }
}
