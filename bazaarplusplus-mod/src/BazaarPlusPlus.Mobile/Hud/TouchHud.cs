using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Mobile.NativeAdjustment;
using BazaarPlusPlus.Mobile.Previews;

namespace BazaarPlusPlus.Mobile.Hud;

internal sealed record TouchHudData(
    Func<string, OverlayRequestOutcome> OpenCollection,
    Func<string, OverlayRequestOutcome>? OpenLiveBuild,
    TouchCombatClockData? Clock,
    TouchPreviewSwitches? Previews,
    TouchScreenFill? ScreenFill
);

internal sealed class TouchHud : IDisposable
{
    public const string Id = "hud";

    private readonly TouchCombatClock? _clock;
    private readonly TouchPreviewSwitches? _previews;
    private readonly TouchScreenFill? _screenFill;
    private readonly MobilePanelHandle _handle;

    public TouchHud(TouchHudData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        Shortcuts = new TouchShortcuts(data.OpenCollection, data.OpenLiveBuild);
        _screenFill = data.ScreenFill;
        var screenFill = _screenFill;
        _clock = data.Clock is { } clock
            ? new TouchCombatClock(clock, () => screenFill?.BoardTop)
            : null;
        _previews = data.Previews;
        // RequestOpen would close the current overlay before the HUD's own open fails (ADR-0022).
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

    private void Tick()
    {
        // Screen Fill and preview switches isolate failures so one HUD adjustment cannot skip another.
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

    public void Dispose()
    {
        _handle.Dispose();
        Shortcuts.Dispose();
        _clock?.Dispose();
        _previews?.Dispose();
    }
}
