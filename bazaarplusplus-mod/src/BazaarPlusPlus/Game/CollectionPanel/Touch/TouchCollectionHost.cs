#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Touch;

/// <summary>
/// The Collection panel's Unity side on a mobile platform (ADR-0024), in place of the desktop's
/// <see cref="CollectionPanel"/>: it registers <see cref="TouchCollectionPanel"/> with the Overlay
/// Panel Host under the same panel id, builds the panel and its touch canvas on the first open
/// (the game's font is loaded by then), ticks the card rental, and warms the shared catalog while
/// the panel is closed, as the desktop panel does.
/// </summary>
internal sealed class TouchCollectionHost : MonoBehaviour
{
    private const string OverlayPanelId = "CollectionPanel";
    private const float CatalogWarmupFrameBudgetMs = 1f;

    private static TouchCollectionHost? _instance;

    private IBppServices _services = null!;
    private CollectionCatalog _catalog = null!;
    private GameDataDayTierResolver _dayTiers = null!;
    private IOverlayPanelHandle? _handle;
    private GameObject? _canvas;
    private TouchCollectionPanel? _panel;

    public void Initialize(
        IBppServices services,
        BppStaticCardMapProvider cardMapProvider,
        GameDataDayTierResolver dayTiers,
        DesktopOverlayPanels overlayHost
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dayTiers = dayTiers ?? throw new ArgumentNullException(nameof(dayTiers));
        _catalog = new CollectionCatalog(cardMapProvider);
        _instance = this;
        _handle = overlayHost.Register(
            new OverlayPanelRegistration(OverlayPanelId, Open, Close, Tick)
            {
                OnSceneChanged = () => TouchCardRental.ReleaseAll("scene_changed"),
                IsModalInputActive = () => _panel?.HasModalBackStep == true,
                DismissModal = () => _panel?.DismissModalBackStep(),
            },
            BppHotkeyActionId.ToggleCollectionPanel
        );
    }

    /// <summary>The dock button's open on a mobile platform; false when this host is not mounted.</summary>
    internal static bool TryOpenFromDockButton()
    {
        if (_instance?._handle == null)
            return false;
        var outcome = _instance._handle.RequestOpen();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.CollectionPanel, "collection_panel.touch.dock_open"),
            ("outcome", outcome)
        );
        return true;
    }

    /// <summary>The Chinese mode changed: the catalog's labels are built again on the next open.</summary>
    internal static void NotifyLocaleChanged()
    {
        if (_instance == null)
            return;
        _instance._catalog.InvalidateCache(CollectionPanelLogReasonCode.LocaleChange);
    }

    private bool Open()
    {
        if (_panel == null && !TryBuild())
            return false;
        return _panel!.Open();
    }

    private bool TryBuild()
    {
        var font = TouchFonts.Body();
        if (font == null)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.touch.font_unavailable"
                )
            );
            return false;
        }
        _canvas = TouchUiKit.CreateOverlayCanvas("BPP.TouchCollection", TouchUiKit.SortingOrder);
        _panel = new TouchCollectionPanel(
            _canvas.transform,
            font,
            _catalog,
            _services,
            _dayTiers,
            _ => _handle?.RequestClose()
        );
        return true;
    }

    private void Close(OverlayCloseReason reason)
    {
        _panel?.Close(reason.ToString());
    }

    private void Tick(float deltaTime, bool isVisible)
    {
        TouchCardRental.Tick();
        if (!isVisible)
        {
            // The desktop panel warms the same catalog while closed; the first tap then opens on it.
            _catalog.AdvanceBuild(CatalogWarmupFrameBudgetMs, out _);
            return;
        }
        _panel?.Tick();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            _panel?.FlushHeroPreference();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        _panel?.Close("destroyed");
        TouchCardRental.ReleaseAll("destroyed");
        if (_canvas != null)
            Destroy(_canvas);
    }
}
