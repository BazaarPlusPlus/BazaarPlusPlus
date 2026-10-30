#nullable enable
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Touch;

/// <summary>
/// Ten-Win Builds' Unity side on a mobile platform (ADR-0024), in place of the desktop's
/// <see cref="LiveBuildPanel"/>: it registers <see cref="TouchLiveBuildPanel"/> with the Overlay
/// Panel Host under the same panel id and builds the panel and its touch canvas on the first open.
/// </summary>
internal sealed class TouchLiveBuildHost : MonoBehaviour
{
    private const string OverlayPanelId = "LiveBuildPanel";

    private static TouchLiveBuildHost? _instance;

    private TouchLiveBuildData _data = null!;
    private IOverlayPanelHandle? _handle;
    private GameObject? _canvas;
    private TouchLiveBuildPanel? _panel;

    public void Initialize(
        BuildRecommendationRepository recommendations,
        DesktopOverlayPanels overlayHost
    )
    {
        TouchMainThread.Capture();
        _data = new TouchLiveBuildData(recommendations);
        _instance = this;
        _handle = overlayHost.Register(
            new OverlayPanelRegistration(OverlayPanelId, Open, Close, Tick)
            {
                IsModalInputActive = () => _panel?.IsInspecting == true,
                DismissModal = () => _panel?.CloseInspection(),
            },
            BppHotkeyActionId.ToggleLiveBuildPanel
        );
        _data.Warm("mounted");
    }

    /// <summary>The dock button's open on a mobile platform; false when this host is not mounted.</summary>
    internal static bool TryOpenFromDockButton()
    {
        if (_instance?._handle == null)
            return false;
        var outcome = _instance._handle.RequestOpen();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.LiveBuildPanel, "live_build_panel.touch.dock_open"),
            ("outcome", outcome)
        );
        return true;
    }

    private bool Open()
    {
        if (_panel == null && !TryBuild())
            return false;
        return _panel!.Open("dock_button");
    }

    private bool TryBuild()
    {
        var font = TouchFonts.Body();
        if (font == null)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.touch.font_unavailable"
                )
            );
            return false;
        }
        _canvas = TouchUiKit.CreateOverlayCanvas("BPP.TouchLiveBuild", TouchUiKit.SortingOrder);
        _panel = new TouchLiveBuildPanel(
            _canvas.transform,
            font,
            _data,
            _ => _handle?.RequestClose()
        );
        return true;
    }

    private void Close(OverlayCloseReason reason) => _panel?.Close(reason.ToString());

    private void Tick(float deltaTime, bool isVisible)
    {
        if (isVisible)
            _panel?.Tick();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        _handle?.Dispose();
        _panel?.Dispose();
        _data.Stop();
        if (_canvas != null)
            Destroy(_canvas);
    }
}
