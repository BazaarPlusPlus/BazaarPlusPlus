#nullable enable
namespace BazaarPlusPlus.Core.Ui;

public readonly record struct NativeHudState(
    bool HasPopup,
    bool IsInspectingCard,
    bool IsEndOfRun,
    bool IsChangingScene
);

public static class HudVisibility
{
    public static bool TouchShortcuts(
        NativeHudState native,
        bool panelOpen,
        bool playing,
        bool anchorAvailable
    ) => anchorAvailable && !playing && TouchControls(native, panelOpen);

    public static bool TouchCombatClock(NativeHudState native, bool panelOpen, bool eligible) =>
        eligible && TouchControls(native, panelOpen);

    public static bool DesktopDockButton(
        bool endOfRun,
        bool screenshotSuppressed,
        bool enabled,
        bool layoutAvailable
    ) => enabled && layoutAvailable && !endOfRun && !screenshotSuppressed;

    private static bool TouchControls(NativeHudState native, bool panelOpen) =>
        !native.IsEndOfRun
        && !panelOpen
        && !native.HasPopup
        && !native.IsInspectingCard
        && !native.IsChangingScene;
}
