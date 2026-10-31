#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Collection;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Custom mountable for CollectionPanel: ComponentMount<T> would attach the MonoBehaviour but
// could not subscribe to ChineseLocaleModeChanged, which the catalog cache + UI labels need
// in order to regenerate when the user cycles BPP's Chinese variant. On a mobile platform
// (ADR-0024) it mounts the Mobile UI's TouchCollectionEntry (ADR-0025) instead, under a touch
// canvas of its own, which the dock button opens through TryOpenTouchPanel.
internal sealed class CollectionPanelMount : IBppMountable
{
    private static TouchCollectionEntry? _touch;

    private readonly Func<DesktopOverlayPanels?> _overlayHost;
    private readonly BppStaticCardMapProvider _cardMapProvider;
    private readonly INativeCardPreviewHost _nativeCardPreviewHost;
    private readonly GameDataDayTierResolver _dayTierResolver;
    private readonly CollectionInspectionModule _inspection;
    private IDisposable? _localeChangedSubscription;
    private GameObject? _touchCanvas;

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

    /// <summary>The dock button's open on a mobile platform; false when the touch panel is not mounted.</summary>
    internal static bool TryOpenTouchPanel()
    {
        if (_touch == null)
            return false;
        var outcome = _touch.RequestOpen("dock_button");
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.CollectionPanel, "collection_panel.touch.dock_open"),
            ("outcome", outcome)
        );
        return true;
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
            var catalog = new DesktopCollectionCardCatalog(new CollectionCatalog(_cardMapProvider));
            _touch = new TouchCollectionEntry(
                new TouchCollectionData(
                    catalog,
                    StaticCollectionSourceCatalog.Instance,
                    new CollectionDayTierSource(_dayTierResolver),
                    new CollectionPanelHeroPreferenceStore(),
                    new DesktopCollectionRunReads(services),
                    services.EncounterState,
                    new DesktopSupporterRotation()
                ),
                TouchCanvas
            );
            host.AddComponent<CollectionTouchLifecycle>().Initialize(_touch, catalog);
            _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
                catalog.InvalidateForLocale()
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
        var touch = host.GetComponent<CollectionTouchLifecycle>();
        if (touch != null)
            UnityEngine.Object.DestroyImmediate(touch);
        _touch?.Dispose();
        _touch = null;
        if (_touchCanvas != null)
            UnityEngine.Object.Destroy(_touchCanvas);
        _touchCanvas = null;
    }

    private Transform TouchCanvas()
    {
        if (_touchCanvas == null)
            _touchCanvas = UiKit.CreateOverlayCanvas("BPP.TouchCollection", UiKit.SortingOrder);
        return _touchCanvas.transform;
    }
}
