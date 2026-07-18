#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelDockButtonController
    : MonoBehaviour,
        IBppNativeSettingsButtonCloneOwner
{
    private const string LogCategory = "CollectionPanelDockButton";
    private const int ScreenResizeSyncFrameCount = 6;
    private const int LayoutImmediateSyncFrameCount = 2;

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
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

        var existingController = anchorButton.GetComponent<CollectionPanelDockButtonController>();
        if (existingController != null && existingController._dockButtonRect != null)
        {
            existingController.SyncDockButtonPlacement();
            return;
        }

        var dockButton = BppNativeSettingsButtonClone.FindOrCreate(anchorButton, placement);
        if (dockButton == null)
            return;

        var controller =
            existingController
            ?? anchorButton.gameObject.AddComponent<CollectionPanelDockButtonController>();
        controller.Initialize(anchorButton, placement, dockButton);
    }

    internal static IDisposable? BeginScreenshotSuppression()
    {
        var controllers = FindObjectsOfType<CollectionPanelDockButtonController>(
            includeInactive: true
        );
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
        RectTransform dockButton
    )
    {
        _anchorButton = anchorButton;
        _dockButtonRect = dockButton;
        _dockButton = dockButton.GetComponent<Button>();
        if (_dockButton == null)
        {
            BppLog.ErrorEvent(
                CollectionPanelLogEvents.DockButtonSetupFailed,
                CollectionPanelLogEvents.DockButtonSetupFailedPlacement.Bind(placement.Key),
                CollectionPanelLogEvents.DockButtonSetupFailedReasonCode.Bind(
                    CollectionPanelLogReasonCode.ButtonMissing
                )
            );
            return;
        }

        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(OnDockButtonClicked);

        SyncDockButtonPlacement();
    }

    private void OnEnable() => SyncDockButtonPlacement();

    private void LateUpdate()
    {
        var shouldSync = _layoutSync.ShouldSync(
            SceneManager.GetActiveScene().name,
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

        var available = _screenLayout.TryResolveAndApplyCollection(
            _anchorButton,
            _dockButtonRect,
            BppSettingsDockPlacement.DefaultSiblingGap,
            out var blockerName
        );
        SetLayoutAvailable(available);
        _layoutLogState.Observe(ToLayoutObservation(available, blockerName));
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
    }

    private void OnDockButtonClicked()
    {
        CollectionPanel.OpenFromDockButton();
    }

    private sealed class ScreenshotSuppressionLease(CollectionPanelDockButtonController controller)
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
