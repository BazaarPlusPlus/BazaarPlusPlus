using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile;
using BazaarPlusPlus.Mobile.Collection;
using BazaarPlusPlus.Mobile.Hud;
using BazaarPlusPlus.Mobile.LiveBuild;
using BazaarPlusPlus.Mobile.NativeAdjustment;
using BazaarPlusPlus.Mobile.Previews;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Android.Host;

internal sealed class AndroidTouchUi
{
    private const string MenuSource = "native_menu";

    private readonly AndroidOverlayPanels _panels;
    private readonly Action<string>? _openHistory;
    private readonly TouchLiveBuildData? _liveBuildData;
    private readonly TouchCollectionEntry _collection;
    private readonly AndroidCollectionCatalog _collectionCatalog;
    private readonly TouchLiveBuildEntry? _liveBuild;
    private readonly TouchHud _hud;

    private GameObject? _root;
    private AndroidNavigationMenuEntry? _historyMenu;
    private AndroidFightMenuEntry? _historyFightMenu;
    private bool _mountFailed;

    public AndroidTouchUi(
        AndroidOverlayPanels panels,
        Action<string>? openHistory,
        TouchLiveBuildData? liveBuildData,
        AndroidSupporterData? supporters,
        AndroidCollectionCatalog collectionCatalog,
        TouchCombatClockData? clock,
        TouchPreviewSwitches? previews,
        TouchScreenFill? screenFill
    )
    {
        _panels = panels ?? throw new ArgumentNullException(nameof(panels));
        _collectionCatalog =
            collectionCatalog ?? throw new ArgumentNullException(nameof(collectionCatalog));
        _openHistory = openHistory;
        _liveBuildData = liveBuildData;
        var run = new AndroidCollectionRunReads();
        _collection = new TouchCollectionEntry(
            new TouchCollectionData(
                collectionCatalog,
                new EmbeddedCollectionSourceCatalog(AndroidCardFacets.IsHeroAvailable),
                new AndroidCollectionDayTierSource(run),
                new AndroidCollectionHeroPreferenceStore(),
                run,
                new AndroidEncounterState(),
                supporters
            ),
            () => _root?.transform
        );
        if (liveBuildData != null)
            _liveBuild = new TouchLiveBuildEntry(liveBuildData, () => _root?.transform);
        var liveBuild = _liveBuild;
        _hud = new TouchHud(
            new TouchHudData(
                _collection.RequestOpen,
                liveBuild == null ? null : liveBuild.RequestOpen,
                clock,
                previews,
                screenFill
            )
        );
    }

    public BppLogField[] Summary() =>
        [
            ("touch_icon_relayouts", _hud.Shortcuts.Relayouts),
            ("live_build_opened", _liveBuild?.Panel?.Opened ?? 0),
            ("live_build_closed", _liveBuild?.Panel?.Closed ?? 0),
            ("live_build_leases", _liveBuild?.Panel?.Leases ?? 0),
            ("live_build_refreshes", _liveBuild?.Panel?.Refreshes ?? 0),
            ("live_build_refreshes_completed", _liveBuild?.Panel?.RefreshesCompleted ?? 0),
            ("live_build_relayouts", _liveBuild?.Panel?.Relayouts ?? 0),
            ("collection_relayouts", _collection.Panel?.Relayouts ?? 0),
            .. _panels.BackSummary(),
        ];

    public void OnAppPaused() =>
        Guarded("touch_ui:pause_collection", _collection.FlushHeroPreference);

    public void EnsureMounted()
    {
        if (_root != null || _mountFailed)
            return;
        if (AndroidGameFont.Font == null)
            return;
        try
        {
            Mount();
            BppLog.InfoEvent(new BppLogEvent(BppLogFeatureScope.Android, "android.ui.mounted"));
        }
        catch (Exception exception)
        {
            _mountFailed = true;
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.ui.mount_failed"),
                exception
            );
        }
    }

    public void Dispose()
    {
        Guarded("touch_ui:dispose_collection", _collection.Dispose);
        Guarded("touch_ui:dispose_live_build", () => _liveBuild?.Dispose());
        Guarded("touch_ui:dispose_hud", _hud.Dispose);
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
    }

    private static void Guarded(string probe, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError(probe, exception);
        }
    }

    private void Mount()
    {
        _root = UiKit.CreateOverlayCanvas("BPP.TouchUi", UiKit.SortingOrder);
        if (_openHistory != null)
        {
            Func<bool> offered = () => true;
            Action open = () =>
                AndroidUiActions.Run("open_History", () => _openHistory(MenuSource));
            _historyMenu = new AndroidNavigationMenuEntry(
                "History",
                HistoryPanelText.Title,
                _hud.Shortcuts.Icons.History,
                offered,
                open
            );
            _historyFightMenu = new AndroidFightMenuEntry(
                "History",
                HistoryPanelText.Title,
                offered,
                () => !MobilePlatform.Port.PlayState().IsPlaying,
                open
            );
        }
    }

    public void Tick()
    {
        if (_root == null)
            return;
        var playing = MobilePlatform.Port.PlayState().IsPlaying;
        if (_liveBuild != null && !_liveBuildData!.Warmed)
            _liveBuildData.Warm("touch_ui");
        if (
            !playing
            && _panels.OpenPanelId != TouchCollectionEntry.PanelId
            && _collectionCatalog.IsBuilding
        )
            _collectionCatalog.Advance(AndroidCollectionCatalogPace.Warm);
        _historyMenu?.Tick();
        _historyFightMenu?.Tick();
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            _panels.TryConsumeBack("keyboard");
    }
}
