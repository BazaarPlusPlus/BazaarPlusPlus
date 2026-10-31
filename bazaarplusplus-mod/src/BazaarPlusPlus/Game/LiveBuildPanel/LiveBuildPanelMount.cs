#nullable enable

using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.LiveBuild;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

/// <summary>
/// Mounts Ten-Win Builds: the desktop's <see cref="LiveBuildPanel"/>, or on a mobile platform
/// (ADR-0024) the Mobile UI's <see cref="TouchLiveBuildEntry"/> (ADR-0025) under a touch canvas of
/// its own, which the dock button opens through <see cref="TryOpenTouchPanel"/>.
/// </summary>
internal sealed class LiveBuildPanelMount : IBppMountable
{
    private static TouchLiveBuildEntry? _touch;

    private readonly Func<DesktopOverlayPanels?> _overlayHost;
    private readonly BuildRecommendationRepository _recommendations;
    private TouchLiveBuildData? _touchData;
    private GameObject? _touchCanvas;

    public LiveBuildPanelMount(
        Func<DesktopOverlayPanels?> overlayHost,
        BuildRecommendationRepository recommendations
    )
    {
        _overlayHost = overlayHost;
        _recommendations = recommendations;
    }

    /// <summary>The dock button's open on a mobile platform; false when the touch panel is not mounted.</summary>
    internal static bool TryOpenTouchPanel()
    {
        if (_touch == null)
            return false;
        var outcome = _touch.RequestOpen("dock_button");
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.LiveBuildPanel, "live_build_panel.touch.dock_open"),
            ("outcome", outcome)
        );
        return true;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var overlayHost = _overlayHost();
        if (overlayHost == null)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.LiveBuildPanel, "live_build_panel.mount.failed"),
                ("reason_code", LiveBuildMountFailureReasonCode.OverlayHostUnavailable)
            );
            return;
        }

        if (TouchPlatform.IsMobile)
        {
            // ADR-0024: on a mobile platform Ten-Win Builds is the touch panel.
            _touchData = new TouchLiveBuildData(_recommendations, new DesktopSupporterRotation());
            _touch = new TouchLiveBuildEntry(_touchData, TouchCanvas);
            _touchData.Warm("mounted");
            return;
        }

        var panel = host.AddComponent<LiveBuildPanel>();
        panel.Initialize(_recommendations, overlayHost);
    }

    public void Unmount(GameObject host)
    {
        var panel = host.GetComponent<LiveBuildPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
        _touch?.Dispose();
        _touch = null;
        _touchData?.Stop();
        _touchData = null;
        if (_touchCanvas != null)
            UnityEngine.Object.Destroy(_touchCanvas);
        _touchCanvas = null;
    }

    private Transform TouchCanvas()
    {
        if (_touchCanvas == null)
            _touchCanvas = UiKit.CreateOverlayCanvas("BPP.TouchLiveBuild", UiKit.SortingOrder);
        return _touchCanvas.transform;
    }
}
