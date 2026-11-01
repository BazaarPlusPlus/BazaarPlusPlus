using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;

namespace BazaarPlusPlus.Mobile.History;

/// <summary>
/// History as a Main Overlay Panel on a mobile platform: it builds <see cref="TouchHistoryPanel"/>
/// for the host's lifetime, since the panel's coordinator returns from a replay while the panel
/// is closed, and registers it with the host's Overlay Panel Host under <see cref="PanelId"/>
/// through the Mobile Platform Port, with no toggle hotkey. The Overlay Panel Host ticks it every
/// frame and closes it. The host opens it from its own entries (the lobby's MENU grid, the in-run
/// menu and the settings row on Android, the game menu's row and the settings row on iOS) with
/// <see cref="Open"/>.
/// </summary>
internal sealed class TouchHistoryEntry : IDisposable
{
    /// <summary>The panel's id on both hosts.</summary>
    public const string PanelId = "history";

    private readonly TouchHistoryPanel _panel;
    private readonly MobilePanelHandle _handle;
    private bool _disposed;

    /// <param name="data">The host services the panel reads.</param>
    public TouchHistoryEntry(TouchHistoryData data)
    {
        _panel = new TouchHistoryPanel(
            data,
            reason => _handle!.RequestOpen(reason),
            reason => _handle!.RequestClose(reason)
        );
        try
        {
            _handle = MobilePanelHandle.Register(handle => new OverlayPanelRegistration(
                PanelId,
                onOpen: () => _panel.Open(handle.OpenSource),
                onClose: reason => _panel.Hide(handle.Describe(reason)),
                tick: _panel.Tick
            )
            {
                // The panel stays open across scenes, except into a combat, as on the desktop.
                SceneChangeClose = SceneChangeClosePolicy.OnlyWhenInCombat,
                IsModalInputActive = () => _panel.HasModalBackStep,
                DismissModal = _panel.DismissModalBackStep,
                OnSceneChanged = _panel.OnSceneChanged,
            });
        }
        catch
        {
            _panel.Dispose();
            throw;
        }
    }

    public bool IsVisible => _handle.IsVisible;

    /// <summary>The panel's counts for the host's periodic hooks summary.</summary>
    public BppLogField[] Summary() => _panel.Summary();

    /// <summary>Main thread: opens the panel unless a combat is running or no font is ready.</summary>
    public void Open(string source) =>
        UiActions.Run(
            "history_open",
            () =>
            {
                if (_handle.RequestOpen(source) == OverlayRequestOutcome.SuppressedByCombat)
                    BppLog.InfoEvent(
                        new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.open_skipped"),
                        ("reason_code", "combat_active")
                    );
            }
        );

    /// <summary>Main thread, after a locale change: an open panel renders its text again.</summary>
    public void RefreshLocalization() => _panel.RefreshLocalization();

    /// <summary>
    /// The host stops: the panel leaves the Overlay Panel Host, then gives its cards back and its
    /// coordinator ends.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _handle.Dispose();
        _panel.Dispose();
    }
}
