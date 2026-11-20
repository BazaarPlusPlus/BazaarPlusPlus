using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;

namespace BazaarPlusPlus.Mobile.History;

// Keep the panel alive while closed so its coordinator can reopen it after a replay.
internal sealed class TouchHistoryEntry : IDisposable
{
    public const string PanelId = "history";

    private readonly TouchHistoryPanel _panel;
    private readonly MobilePanelHandle _handle;
    private bool _disposed;

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

    public BppLogField[] Summary() => _panel.Summary();

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

    public void RefreshLocalization() => _panel.RefreshLocalization();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _handle.Dispose();
        _panel.Dispose();
    }
}
