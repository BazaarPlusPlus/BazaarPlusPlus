#nullable enable

using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.LiveBuildPanel.Touch;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildPanelMount : IBppMountable
{
    private readonly Func<DesktopOverlayPanels?> _overlayHost;
    private readonly BuildRecommendationRepository _recommendations;

    public LiveBuildPanelMount(
        Func<DesktopOverlayPanels?> overlayHost,
        BuildRecommendationRepository recommendations
    )
    {
        _overlayHost = overlayHost;
        _recommendations = recommendations;
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
            host.AddComponent<TouchLiveBuildHost>().Initialize(_recommendations, overlayHost);
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
        var touch = host.GetComponent<TouchLiveBuildHost>();
        if (touch != null)
            UnityEngine.Object.DestroyImmediate(touch);
    }
}
