#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Touch;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Custom mountable for CollectionPanel: ComponentMount<T> would attach the MonoBehaviour but
// could not subscribe to ChineseLocaleModeChanged, which the catalog cache + UI labels need
// in order to regenerate when the user cycles BPP's Chinese variant.
internal sealed class CollectionPanelMount : IBppMountable
{
    private readonly Func<DesktopOverlayPanels?> _overlayHost;
    private readonly BppStaticCardMapProvider _cardMapProvider;
    private readonly INativeCardPreviewHost _nativeCardPreviewHost;
    private readonly GameDataDayTierResolver _dayTierResolver;
    private readonly CollectionInspectionModule _inspection;
    private IDisposable? _localeChangedSubscription;

    public CollectionPanelMount(
        Func<DesktopOverlayPanels?> overlayHost,
        BppStaticCardMapProvider cardMapProvider,
        INativeCardPreviewHost nativeCardPreviewHost,
        GameDataDayTierResolver dayTierResolver,
        CollectionInspectionModule inspection
    )
    {
        _overlayHost = overlayHost;
        _inspection = inspection;
        _cardMapProvider =
            cardMapProvider ?? throw new ArgumentNullException(nameof(cardMapProvider));
        _nativeCardPreviewHost =
            nativeCardPreviewHost ?? throw new ArgumentNullException(nameof(nativeCardPreviewHost));
        _dayTierResolver =
            dayTierResolver ?? throw new ArgumentNullException(nameof(dayTierResolver));
    }

    public void Mount(GameObject host, IBppServices services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        DesktopOverlayPanels? overlayHost;
        try
        {
            overlayHost = _overlayHost();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.mount.failed"
                ),
                ex,
                ("reason_code", CollectionPanelLogReasonCode.OverlayHostUnavailable)
            );
            return;
        }
        if (overlayHost == null)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.mount.failed"
                ),
                ("reason_code", CollectionPanelLogReasonCode.OverlayHostUnavailable)
            );
            return;
        }

        if (TouchPlatform.IsMobile)
        {
            // ADR-0024: on a mobile platform Collection is the touch panel.
            host.AddComponent<TouchCollectionHost>()
                .Initialize(services, _cardMapProvider, _dayTierResolver, overlayHost);
            _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
                TouchCollectionHost.NotifyLocaleChanged()
            );
            return;
        }

        var panel = host.AddComponent<CollectionPanel>();
        panel.Initialize(
            services,
            _cardMapProvider,
            _nativeCardPreviewHost,
            _dayTierResolver,
            _inspection
        );
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
        var touch = host.GetComponent<TouchCollectionHost>();
        if (touch != null)
            UnityEngine.Object.DestroyImmediate(touch);
    }
}
