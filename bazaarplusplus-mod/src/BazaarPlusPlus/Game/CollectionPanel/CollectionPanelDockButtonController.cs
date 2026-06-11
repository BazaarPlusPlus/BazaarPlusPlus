#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelDockButtonController
    : MonoBehaviour,
        IBppNativeSettingsButtonCloneOwner
{
    private const string LogCategory = "CollectionPanelDockButton";

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
    private int _screenshotSuppressionCount;
    private BppSettingsDockPlacement _placement;

    internal static void Attach(Button anchorButton, BppSettingsDockPlacement placement)
    {
        if (anchorButton == null)
            return;

        var existingController = anchorButton.GetComponent<CollectionPanelDockButtonController>();
        if (existingController != null && existingController._dockButtonRect != null)
        {
            existingController.SyncDockButtonPlacement();
            existingController.ApplyScreenshotSuppressionVisibility();
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
        _placement = placement;
        _dockButtonRect = dockButton;
        _dockButton = dockButton.GetComponent<Button>();
        if (_dockButton == null)
        {
            BppLog.Warn(LogCategory, $"Clone '{placement.Key}' has no neutral Button.");
            return;
        }

        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(OnDockButtonClicked);

        SyncDockButtonPlacement();
        ApplyScreenshotSuppressionVisibility();
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
        var shouldBeVisible = _screenshotSuppressionCount == 0;
        if (_dockButtonRect != null && _dockButtonRect.gameObject.activeSelf != shouldBeVisible)
            _dockButtonRect.gameObject.SetActive(shouldBeVisible);
    }

    private void OnDockButtonClicked()
    {
        CollectionPanel.OpenFromDockButton();
    }

    private void OnRectTransformDimensionsChange()
    {
        SyncDockButtonPlacement();
    }

    private void SyncDockButtonPlacement()
    {
        if (_anchorButton == null || _dockButtonRect == null)
            return;

        var parentRect = _dockButtonRect.parent as RectTransform;
        var anchorRect = _anchorButton.transform as RectTransform;
        if (parentRect == null || anchorRect == null)
            return;

        var corners = new Vector3[4];
        anchorRect.GetWorldCorners(corners);

        var centerWorld = (corners[0] + corners[2]) * 0.5f;
        var leftWorld = (corners[0] + corners[1]) * 0.5f;
        var rightWorld = (corners[2] + corners[3]) * 0.5f;
        var topWorld = (corners[1] + corners[2]) * 0.5f;
        var bottomWorld = (corners[0] + corners[3]) * 0.5f;

        var centerLocal = parentRect.InverseTransformPoint(centerWorld);
        var leftLocal = parentRect.InverseTransformPoint(leftWorld);
        var rightLocal = parentRect.InverseTransformPoint(rightWorld);
        var topLocal = parentRect.InverseTransformPoint(topWorld);
        var bottomLocal = parentRect.InverseTransformPoint(bottomWorld);

        var dockPosition = BppSettingsDockGeometry.CalculateDockButtonLocalPosition(
            centerLocal.x,
            centerLocal.y,
            leftLocal.x,
            rightLocal.x,
            topLocal.y,
            bottomLocal.y,
            _dockButtonRect.localPosition.z,
            _placement
        );
        _dockButtonRect.localPosition = new Vector3(dockPosition.X, dockPosition.Y, dockPosition.Z);
        _dockButtonRect.localRotation = Quaternion.identity;
        _dockButtonRect.SetAsLastSibling();
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
