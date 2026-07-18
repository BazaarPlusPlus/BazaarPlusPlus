#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Custom mountable for CollectionPanel: ComponentMount<T> would attach the MonoBehaviour but
// could not subscribe to ChineseLocaleModeChanged, which the catalog cache + UI labels need
// in order to regenerate when the user cycles BPP's Chinese variant.
internal sealed class CollectionPanelMount : IBppMountable
{
    private readonly Func<OverlayPanelHost?> _overlayHost;
    private readonly BppStaticCardMapProvider _cardMapProvider;
    private readonly INativeCardPreviewHost _nativeCardPreviewHost;
    private IDisposable? _localeChangedSubscription;

    public CollectionPanelMount(
        Func<OverlayPanelHost?> overlayHost,
        BppStaticCardMapProvider cardMapProvider,
        INativeCardPreviewHost nativeCardPreviewHost
    )
    {
        _overlayHost = overlayHost;
        _cardMapProvider =
            cardMapProvider ?? throw new ArgumentNullException(nameof(cardMapProvider));
        _nativeCardPreviewHost =
            nativeCardPreviewHost ?? throw new ArgumentNullException(nameof(nativeCardPreviewHost));
    }

    public void Mount(GameObject host, IBppServices services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        OverlayPanelHost? overlayHost;
        try
        {
            overlayHost = _overlayHost();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                CollectionPanelLogEvents.MountFailed,
                ex,
                CollectionPanelLogEvents.MountFailedReasonCode.Bind(
                    CollectionPanelLogReasonCode.OverlayHostUnavailable
                )
            );
            return;
        }
        if (overlayHost == null)
        {
            BppLog.ErrorEvent(
                CollectionPanelLogEvents.MountFailed,
                CollectionPanelLogEvents.MountFailedReasonCode.Bind(
                    CollectionPanelLogReasonCode.OverlayHostUnavailable
                )
            );
            return;
        }

        var panel = host.AddComponent<CollectionPanel>();
        panel.Initialize(services, _cardMapProvider, _nativeCardPreviewHost);
        panel.AttachToOverlayHost(overlayHost);

        _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
            CollectionPanel.NotifyLocaleChanged()
        );
    }

    public void Unmount(GameObject host)
    {
        _localeChangedSubscription?.Dispose();
        _localeChangedSubscription = null;

        var panel = host.GetComponent<CollectionPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
