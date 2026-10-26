#nullable enable
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.GameInterop.Scenes;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.UI;
using CollectionPanelFeature = BazaarPlusPlus.Game.CollectionPanel.CollectionPanel;
using LiveBuildPanelFeature = BazaarPlusPlus.Game.LiveBuildPanel.LiveBuildPanel;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppPanelDockButtonsController
    : MonoBehaviour,
        IBppNativeSettingsButtonCloneOwner
{
    private const int ScreenResizeSyncFrameCount = 6;
    private const int LayoutImmediateSyncFrameCount = 2;

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
    private RectTransform? _liveBuildButtonRect;
    private bool _hasAvailableLiveBuildLayout;
    private readonly BppScreenResizeSyncTracker _screenResizeSync = new(ScreenResizeSyncFrameCount);
    private readonly BppDockLayoutSyncTracker _layoutSync = new(LayoutImmediateSyncFrameCount);
    private readonly BppDockButtonScreenLayout _screenLayout = new();
    private readonly CollectionPanelDockLayoutLogState _layoutLogState = new();
    private bool _hasAvailableDockLayout;
    private int _screenshotSuppressionCount;

    internal RectTransform? DockButtonRect => _dockButtonRect;

    internal void SetLayoutAvailable(bool available)
    {
        _hasAvailableDockLayout = available;
        ApplyScreenshotSuppressionVisibility();
    }

    internal static void Attach(Button anchorButton, BppSettingsDockPlacement placement)
    {
        if (anchorButton == null)
            return;

        // Ten-Win Builds reads the run's board, so only the run's settings button carries it.
        var inRun = placement.ButtonId == SettingsNativeButtonId.FightMenu;
        var existingController = anchorButton.GetComponent<BppPanelDockButtonsController>();
        if (
            existingController != null
            && existingController._dockButtonRect != null
            && (existingController._liveBuildButtonRect != null || !inRun)
        )
        {
            existingController.SyncDockButtonPlacement();
            return;
        }

        var dockButton = BppNativeSettingsButtonClone.FindOrCreate(anchorButton, placement);
        if (dockButton == null)
            return;

        RectTransform? liveBuildButton = null;
        if (inRun)
        {
            liveBuildButton = BppNativeSettingsButtonClone.FindOrCreate(
                anchorButton,
                BppSettingsDockPlacement.ForButton(
                    $"TenWinBuilds_{placement.Key}",
                    placement.ButtonId
                ),
                BppDockButtonSpriteId.TenWinBuilds
            );
            if (liveBuildButton == null)
                return;
        }

        var controller =
            existingController
            ?? anchorButton.gameObject.AddComponent<BppPanelDockButtonsController>();
        controller.Initialize(anchorButton, placement, dockButton, liveBuildButton);
    }

    internal static IDisposable? BeginScreenshotSuppression()
    {
        var controllers = FindObjectsOfType<BppPanelDockButtonsController>(includeInactive: true);
        if (controllers.Length == 0)
            return null;

        var suppressionActions = new Func<IDisposable?>[controllers.Length];
        for (var index = 0; index < controllers.Length; index++)
            suppressionActions[index] = controllers[index].BeginInstanceScreenshotSuppression;

        return UiSuppressionScope.Begin(suppressionActions);
    }

    private void Initialize(
        Button anchorButton,
        BppSettingsDockPlacement placement,
        RectTransform dockButton,
        RectTransform? liveBuildButton
    )
    {
        _anchorButton = anchorButton;
        _dockButtonRect = dockButton;
        _liveBuildButtonRect = liveBuildButton;
        _dockButton = dockButton.GetComponent<Button>();
        if (_dockButton == null)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.dock_button.setup_failed"
                ),
                ("placement", placement.Key),
                ("reason_code", CollectionPanelLogReasonCode.ButtonMissing)
            );
            return;
        }

        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(CollectionPanelFeature.OpenFromDockButton);
        if (liveBuildButton?.GetComponent<Button>() is { } liveBuildAction)
        {
            liveBuildAction.onClick.RemoveAllListeners();
            liveBuildAction.onClick.AddListener(LiveBuildPanelFeature.OpenFromDockButton);
        }

        SyncDockButtonPlacement();
    }

    private void OnEnable() => SyncDockButtonPlacement();

    private void LateUpdate()
    {
        var shouldSync = _layoutSync.ShouldSync(
            ActiveSceneNameCache.Current,
            Time.realtimeSinceStartup
        );
        shouldSync |= _screenResizeSync.ShouldSync(Screen.width, Screen.height);
        if (shouldSync)
            SyncDockButtonPlacement();
    }

    private void OnRectTransformDimensionsChange() => SyncDockButtonPlacement();

    private void SyncDockButtonPlacement()
    {
        if (_anchorButton == null || _dockButtonRect == null)
            return;

        SyncDockCanvas(_dockButtonRect);
        if (_liveBuildButtonRect != null)
            SyncDockCanvas(_liveBuildButtonRect);
        var available = _screenLayout.TryResolveAndApplyCollection(
            _anchorButton,
            _dockButtonRect,
            BppSettingsDockPlacement.DefaultSiblingGap,
            out var blockerName
        );
        _hasAvailableLiveBuildLayout =
            _liveBuildButtonRect != null
            && _screenLayout.TryResolveAndApplyCollection(
                _anchorButton,
                _liveBuildButtonRect,
                BppSettingsDockPlacement.DefaultSiblingGap,
                out _,
                occupiedRect: available ? _dockButtonRect : null,
                beside: true
            );
        SetLayoutAvailable(available);
        _layoutLogState.Observe(ToLayoutObservation(available, blockerName));
    }

    private void SyncDockCanvas(RectTransform dockButtonRect)
    {
        var anchorCanvas = _anchorButton!.GetComponentInParent<Canvas>();
        if (anchorCanvas == null)
            return;
        while (!anchorCanvas.isRootCanvas && !anchorCanvas.overrideSorting)
        {
            var parentCanvas = anchorCanvas.transform.parent?.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
                break;
            anchorCanvas = parentCanvas;
        }

        // The hero-ready background intercepts clicks below the settings bar.
        // Give only our button a sorting boundary above its native canvas content.
        var canvas =
            dockButtonRect.GetComponent<Canvas>()
            ?? dockButtonRect.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingLayerID = anchorCanvas.sortingLayerID;
        canvas.sortingOrder = anchorCanvas.sortingOrder + 1;
        if (dockButtonRect.GetComponent<GraphicRaycaster>() == null)
            dockButtonRect.gameObject.AddComponent<GraphicRaycaster>();
    }

    private static CollectionPanelDockLayoutObservation ToLayoutObservation(
        bool available,
        string? blockerName
    )
    {
        if (available)
            return CollectionPanelDockLayoutObservation.Available();

        return blockerName switch
        {
            "missing-collection-button" => CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.MissingCollectionButton,
                null
            ),
            "gear-footprint-unavailable" => CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.GearFootprintUnavailable,
                null
            ),
            "collection-footprint-unavailable" => CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.CollectionFootprintUnavailable,
                null
            ),
            "anchor-canvas-unavailable" => CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.AnchorCanvasUnavailable,
                null
            ),
            "target-local-position-unavailable" => CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.TargetLocalPositionUnavailable,
                null
            ),
            _ => CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.PlacementBlocked,
                blockerName
            ),
        };
    }

    private IDisposable BeginInstanceScreenshotSuppression()
    {
        _screenshotSuppressionCount++;
        ApplyScreenshotSuppressionVisibility();
        return new ScreenshotSuppressionLease(this);
    }

    private void EndInstanceScreenshotSuppression()
    {
        if (_screenshotSuppressionCount > 0)
            _screenshotSuppressionCount--;

        ApplyScreenshotSuppressionVisibility();
    }

    private void ApplyScreenshotSuppressionVisibility()
    {
        var shouldBeVisible = _screenshotSuppressionCount == 0 && _hasAvailableDockLayout;
        if (_dockButtonRect != null && _dockButtonRect.gameObject.activeSelf != shouldBeVisible)
            _dockButtonRect.gameObject.SetActive(shouldBeVisible);
        var liveBuildVisible = _screenshotSuppressionCount == 0 && _hasAvailableLiveBuildLayout;
        if (
            _liveBuildButtonRect != null
            && _liveBuildButtonRect.gameObject.activeSelf != liveBuildVisible
        )
            _liveBuildButtonRect.gameObject.SetActive(liveBuildVisible);
    }

    private sealed class ScreenshotSuppressionLease(BppPanelDockButtonsController controller)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            controller.EndInstanceScreenshotSuppression();
        }
    }
}
