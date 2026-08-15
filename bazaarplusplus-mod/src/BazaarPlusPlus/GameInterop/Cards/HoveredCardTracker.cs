#nullable enable
using BazaarGameClient.Domain.Models.Cards;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>
/// The CardController currently under the pointer, fed by the pointer-enter/exit patches.
/// Reads re-validate against the controller's own cursor state (the game clears
/// IsCursorOverCard on Deselect and OnDisable), so a missed exit event — destroyed card,
/// suppressed hover, closed screen — degrades to "no hover" instead of a stale card.
/// Main-thread only.
/// </summary>
internal static class HoveredCardTracker
{
    private static CardController? _hovered;

    internal static void NotifyPointerEnter(CardController? controller)
    {
        if (controller != null)
            _hovered = controller;
    }

    internal static void NotifyPointerExit(CardController? controller)
    {
        if (ReferenceEquals(_hovered, controller))
            _hovered = null;
    }

    internal static Card? TryGetHoveredCard()
    {
        var controller = _hovered;
        if (controller == null)
        {
            // Unity-destroyed controllers compare equal to null; drop the stale handle.
            _hovered = null;
            return null;
        }

        try
        {
            if (!controller.isActiveAndEnabled || !controller.IsCursorOverCard)
                return null;
            return controller.CardData;
        }
        catch
        {
            return null;
        }
    }
}
