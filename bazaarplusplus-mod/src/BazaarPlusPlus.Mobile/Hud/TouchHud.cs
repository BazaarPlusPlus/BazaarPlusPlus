using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Mobile.NativeAdjustment;
using BazaarPlusPlus.Mobile.Previews;

namespace BazaarPlusPlus.Mobile.Hud;

/// <summary>What a host hands the HUD.</summary>
/// <param name="OpenCollection">Opens Collection with the open's source.</param>
/// <param name="OpenLiveBuild">Opens Ten-Win Builds, or null when the host has none.</param>
/// <param name="ShortcutsShown">
/// The host's own rule for the shortcuts beyond the HUD's: nothing of the host's hides them (on
/// the Mono host, an end-of-run screenshot; Android takes none).
/// </param>
/// <param name="Clock">The combat clock's state, or null for no clock.</param>
/// <param name="Previews">
/// The item tooltip's preview switches, which the HUD ticks, clears on a scene change and
/// disposes; null for none.
/// </param>
/// <param name="ScreenFill">
/// Screen Fill, which the HUD ticks and whose board top the clock follows; null for none.
/// </param>
internal sealed record TouchHudData(
    Func<string, OverlayRequestOutcome> OpenCollection,
    Func<string, OverlayRequestOutcome>? OpenLiveBuild,
    Func<bool> ShortcutsShown,
    TouchCombatClockData? Clock,
    TouchPreviewSwitches? Previews,
    TouchScreenFill? ScreenFill
);

/// <summary>
/// The Mobile UI's HUD beside the game's own: the panel shortcuts by the settings button
/// (<see cref="TouchShortcuts"/>), the combat clock by the vault
/// (<see cref="TouchCombatClock"/>) and the preview switches in the item tooltip
/// (<see cref="TouchPreviewSwitches"/>), with Screen Fill (<see cref="TouchScreenFill"/>), which
/// needs the same per-frame tick. It is no Main Overlay Panel, but it registers with the
/// host's Overlay Panel Host through the Mobile Platform Port as the panels do, under
/// <see cref="Id"/> and with a registration that never opens, so the host ticks it every frame
/// and tells it of a scene change, which drops the clock's plaque and the switches (ADR-0025
/// decision 3). Nothing calls <c>RequestOpen</c> on it.
/// </summary>
internal sealed class TouchHud : IDisposable
{
    /// <summary>The HUD's registration id on both hosts.</summary>
    public const string Id = "hud";

    private readonly TouchCombatClock? _clock;
    private readonly TouchPreviewSwitches? _previews;
    private readonly TouchScreenFill? _screenFill;
    private readonly MobilePanelHandle _handle;

    public TouchHud(TouchHudData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        Shortcuts = new TouchShortcuts(
            data.OpenCollection,
            data.OpenLiveBuild,
            data.ShortcutsShown
        );
        _screenFill = data.ScreenFill;
        var screenFill = _screenFill;
        _clock = data.Clock is { } clock
            ? new TouchCombatClock(clock, () => screenFill?.BoardTop)
            : null;
        _previews = data.Previews;
        // Never RequestOpen this handle: the host would close the open panel as superseded, and
        // then the HUD's open fails, so the player loses the panel for nothing (ADR-0022).
        _handle = MobilePanelHandle.Register(_ => new OverlayPanelRegistration(
            Id,
            onOpen: static () => false,
            onClose: static _ => { },
            tick: (_, _) => Tick()
        )
        {
            OnSceneChanged = SceneChanged,
        });
    }

    public TouchShortcuts Shortcuts { get; }

    /// <summary>Hides the shortcuts and the clock until the next tick shows them again.</summary>
    public void Hide()
    {
        Shortcuts.Hide();
        _clock?.Hide();
    }

    private void Tick()
    {
        // First, and catching their own failure: Screen Fill and the switches never skip or are
        // skipped by the shortcuts and the clock.
        _screenFill?.Tick();
        _previews?.Tick();
        var panelOpen = MobilePlatform.Port.OpenPanelId != null;
        Shortcuts.Tick(panelOpen);
        _clock?.Tick(panelOpen);
    }

    private void SceneChanged()
    {
        _clock?.Dispose();
        _previews?.Clear("scene_changed");
    }

    /// <summary>The host stops: the HUD leaves the Overlay Panel Host, then its views go.</summary>
    public void Dispose()
    {
        _handle.Dispose();
        Shortcuts.Dispose();
        _clock?.Dispose();
        _previews?.Dispose();
    }
}
