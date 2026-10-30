#nullable enable

namespace BazaarPlusPlus.GameInterop.Touch;

/// <summary>The native HUD state that hides touch surfaces: an open popup or an inspected card.</summary>
internal static class TouchNativeHud
{
    private static TheBazaar.PopupManager? _popup;

    /// <summary>The game's popup manager, once its scene has made one.</summary>
    public static TheBazaar.PopupManager? Popups
    {
        get
        {
            if (_popup == null)
                _popup = UnityEngine.Object.FindFirstObjectByType<TheBazaar.PopupManager>();
            return _popup;
        }
    }

    public static bool HasPopup =>
        Popups is { } popup && (popup.IsAnyPopupOpen || popup.IsSettingsOpen);

    /// <summary>Whether the native card drawer is showing an inspected card.</summary>
    public static bool IsInspectingCard =>
        TheBazaar.Data.TooltipParentComponent?.CardTooltipController?.CurrentCard != null;
}
