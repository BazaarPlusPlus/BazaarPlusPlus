#nullable enable

using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.OverlayPanels;
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

        var panel = host.AddComponent<LiveBuildPanel>();
        panel.Initialize(_recommendations, overlayHost);
    }

    public void Unmount(GameObject host)
    {
        var panel = host.GetComponent<LiveBuildPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
