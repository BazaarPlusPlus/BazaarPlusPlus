#nullable enable
using System.Collections;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CurrentReplayRecordingButtonController : MonoBehaviour
{
    private const string CloneName = "BPP_CurrentReplayRecordingButton";
    private const float DockButtonGap = BppSettingsDockPlacement.DefaultSiblingGap;
    private Button? _settingsButton;
    private Button? _nativeReplayButton;
    private Button? _nativeRecapButton;
    private Button? _nativeRecapBackButton;
    private Button? _button;
    private RectTransform? _cloneRect;
    private GameObject? _clone;
    private Image? _icon;
    private BppDockButtonSpriteId? _lastSpriteId;
    private Coroutine? _tooltipPositionCoroutine;
    private bool _tooltipHovered;
    private readonly WaitForEndOfFrame _tooltipEndOfFrame = new();
    private readonly Vector3[] _buttonWorldCorners = new Vector3[4];
    private readonly Vector3[] _tooltipWorldCorners = new Vector3[4];
    private readonly BppDockButtonScreenLayout _screenLayout = new();
    private readonly CurrentReplayRecordingUiLogState _uiLogState = new();
    private bool _layoutAvailable;
    private CurrentReplayRecordingUiLayoutReasonCode _layoutReasonCode;

    internal static CurrentReplayRecordingButtonController? Attach(Button settingsButton)
    {
        if (settingsButton == null || settingsButton.transform.parent is not RectTransform host)
            return null;

        var existing = settingsButton.GetComponent<CurrentReplayRecordingButtonController>();
        if (existing != null)
        {
            existing._settingsButton = settingsButton;
            existing.SyncLayout();
            existing.Refresh();
            return existing;
        }

        var existingClone = host.Find(CloneName);
        var clone =
            existingClone == null
                ? Instantiate(settingsButton.gameObject, host, worldPositionStays: false)
                : existingClone.gameObject;
        clone.name = CloneName;
        clone.SetActive(false);
        var controller =
            settingsButton.gameObject.AddComponent<CurrentReplayRecordingButtonController>();
        controller.Initialize(settingsButton, clone);
        return controller;
    }

    internal static void BindNativeActions(
        Button nativeReplayButton,
        Button nativeRecapButton,
        Button nativeRecapBackButton
    )
    {
        foreach (
            var controller in FindObjectsOfType<CurrentReplayRecordingButtonController>(
                includeInactive: true
            )
        )
        {
            controller._nativeReplayButton = nativeReplayButton;
            controller._nativeRecapButton = nativeRecapButton;
            controller._nativeRecapBackButton = nativeRecapBackButton;
            controller.Refresh();
        }
    }

    private void Initialize(Button settingsButton, GameObject clone)
    {
        _settingsButton = settingsButton;
        _clone = clone;
        _cloneRect = clone.transform as RectTransform;
        var nativeButtonController = clone.GetComponent<BazaarButtonController>();
        var nativeVisualState = BppDockButtonVisualState.Capture(
            clone.GetComponent<Button>(),
            nativeButtonController?.DefaultImage
        );
        _icon = StripNativeBehavior(clone);
        var fallbackFrame = clone.GetComponent<Image>();
        if (fallbackFrame == null)
        {
            fallbackFrame = clone.AddComponent<Image>();
            fallbackFrame.color = new Color(1f, 1f, 1f, 0f);
        }
        BppDockButtonVisuals.Apply(clone, _icon, freshClone: true, nativeState: nativeVisualState);
        _button = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(OnClicked);
        _button.navigation = new Navigation { mode = Navigation.Mode.None };

        var layout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var relay = clone.GetComponent<CurrentReplayRecordingButtonHoverRelay>();
        if (relay == null)
            relay = clone.AddComponent<CurrentReplayRecordingButtonHoverRelay>();
        relay.Bind(this);
        ApplyIcon(CurrentReplayRecordingPhase.Ready);

        CombatReplayRuntime.Instance?.PrepareCurrentReplayRecordingAvailability();
        SyncLayout();
        Refresh();
    }

    private void LateUpdate()
    {
        SyncLayout();
        Refresh();
    }

    private void OnDisable()
    {
        HideTooltip();
    }

    private static Image? StripNativeBehavior(GameObject clone)
    {
        var nativeIcon = BppDockButtonVisuals.ResolveNativeIconImage(clone);
        foreach (var custom in clone.GetComponentsInChildren<ButtonCustom>(true))
            DestroyImmediate(custom);
        foreach (var native in clone.GetComponentsInChildren<BazaarButtonController>(true))
            DestroyImmediate(native);
        foreach (var owner in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (owner is IBppNativeSettingsButtonCloneOwner)
                DestroyImmediate(owner);
        foreach (var nested in clone.GetComponentsInChildren<Button>(true))
            if (nested.gameObject != clone)
                DestroyImmediate(nested);

        return nativeIcon;
    }

    private void SyncLayout()
    {
        if (_settingsButton == null || _cloneRect == null)
        {
            _layoutAvailable = false;
            return;
        }

        var anchorButton = ResolveDockAnchorButton(_settingsButton);
        _layoutAvailable = _screenLayout.TryResolveAndApplyCollection(
            anchorButton,
            _cloneRect,
            DockButtonGap,
            out var blockerName
        );
        _layoutReasonCode = CurrentReplayRecordingUiLogState.ResolveLayoutReason(
            _layoutAvailable,
            blockerName
        );
    }

    private static Button ResolveDockAnchorButton(Button settingsButton)
    {
        var collectionRect = settingsButton
            .GetComponent<CollectionPanelDockButtonController>()
            ?.DockButtonRect;
        if (
            collectionRect != null
            && collectionRect.gameObject.activeInHierarchy
            && collectionRect.GetComponent<Button>() is { } collectionButton
        )
        {
            return collectionButton;
        }

        return settingsButton;
    }

    private void Refresh()
    {
        if (_clone == null || _button == null)
            return;
        var snapshot = GetDisplaySnapshot();
        var visible = snapshot.Visible && _layoutAvailable;
        var wasActive = _clone.activeSelf;
        if (_clone.activeSelf != visible)
            _clone.SetActive(visible);
        if (visible && !wasActive)
            _lastSpriteId = null;
        var nativeActionsBound =
            _nativeReplayButton != null
            && _nativeRecapButton != null
            && _nativeRecapBackButton != null;
        _uiLogState.Observe(
            snapshot,
            _layoutAvailable,
            _layoutReasonCode,
            _clone.activeSelf,
            nativeActionsBound,
            _icon != null && _icon.sprite != null
        );
        if (!visible)
            return;

        _button.interactable = nativeActionsBound && (snapshot.CanStart || snapshot.CanReveal);
        ApplyIcon(snapshot.Phase);
    }

    private void OnClicked()
    {
        var runtime = CombatReplayRuntime.Instance;
        var nativeReplayButton = _nativeReplayButton;
        var nativeRecapButton = _nativeRecapButton;
        var nativeRecapBackButton = _nativeRecapBackButton;
        if (
            runtime == null
            || nativeReplayButton == null
            || nativeRecapButton == null
            || nativeRecapBackButton == null
        )
            return;

        var snapshot = runtime.GetCurrentReplayRecordingSnapshot();
        if (snapshot.CanReveal)
            runtime.TryRevealCurrentReplayVideo(out _);
        else if (snapshot.CanStart)
            runtime.TryStartCurrentReplayRecording(
                nativeReplayButton.onClick.Invoke,
                nativeRecapButton.onClick.Invoke,
                nativeRecapBackButton.onClick.Invoke,
                out _
            );
        Refresh();
    }

    internal void ShowTooltip()
    {
        var snapshot = GetDisplaySnapshot();
        if (!snapshot.Visible || _cloneRect == null)
            return;

        _tooltipHovered = true;
        if (_tooltipPositionCoroutine != null)
            StopCoroutine(_tooltipPositionCoroutine);
        Data.TooltipParentComponent?.ShowAuxiliaryTooltipController(
            _cloneRect,
            Vector3.zero,
            CurrentReplayRecordingText.Tooltip(snapshot)
        );
        _tooltipPositionCoroutine = StartCoroutine(PositionTooltipBesideButton());
    }

    private static CurrentReplayRecordingSnapshot GetDisplaySnapshot() =>
        CombatReplayRuntime.Instance?.GetCurrentReplayRecordingSnapshot() ?? default;

    internal void HideTooltip()
    {
        _tooltipHovered = false;
        if (_tooltipPositionCoroutine != null)
        {
            StopCoroutine(_tooltipPositionCoroutine);
            _tooltipPositionCoroutine = null;
        }
        Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
    }

    private IEnumerator PositionTooltipBesideButton()
    {
        while (_tooltipHovered)
        {
            yield return _tooltipEndOfFrame;
            if (!_tooltipHovered || _cloneRect == null)
                break;

            var tooltipParent = Data.TooltipParentComponent;
            var tooltip = tooltipParent?.AuxiliaryTooltipController;
            if (
                tooltip == null
                || tooltipParent == null
                || !tooltipParent.IsAuxiliaryTooltipDisplayed
                || tooltip._coroutine != null
            )
                continue;

            tooltip.PositionOverUI(_cloneRect);
            var tooltipRect = tooltip.PositioningRectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
            _cloneRect.GetWorldCorners(_buttonWorldCorners);
            var tooltipBoundsRect = tooltip._contentForWorldBounds ?? tooltipRect;
            tooltipBoundsRect.GetWorldCorners(_tooltipWorldCorners);

            var buttonCenterX = (_buttonWorldCorners[1].x + _buttonWorldCorners[2].x) * 0.5f;
            var buttonTop = Mathf.Max(_buttonWorldCorners[1].y, _buttonWorldCorners[2].y);
            var tooltipCenterX = (_tooltipWorldCorners[0].x + _tooltipWorldCorners[3].x) * 0.5f;
            var tooltipBottom = Mathf.Min(_tooltipWorldCorners[0].y, _tooltipWorldCorners[3].y);
            var buttonHeight = Mathf.Abs(_buttonWorldCorners[1].y - _buttonWorldCorners[0].y);
            var gap = Mathf.Max(buttonHeight * 0.12f, 8f);
            tooltipRect.position += new Vector3(
                buttonCenterX - tooltipCenterX,
                buttonTop - tooltipBottom + gap,
                0f
            );
            tooltip.KeepTooltipWithinBounds();
        }

        _tooltipPositionCoroutine = null;
    }

    private void ApplyIcon(CurrentReplayRecordingPhase phase)
    {
        if (_icon == null)
            return;

        var spriteId = SpriteId(phase);
        if (_lastSpriteId == spriteId)
            return;

        var sprite = BppDockButtonSpriteProvider.Get(spriteId);
        if (sprite == null)
            return;

        BppDockButtonVisuals.ApplyIcon(_icon, sprite);
        _lastSpriteId = spriteId;
    }

    private static BppDockButtonSpriteId SpriteId(CurrentReplayRecordingPhase phase) =>
        phase switch
        {
            CurrentReplayRecordingPhase.Armed or CurrentReplayRecordingPhase.Recording =>
                BppDockButtonSpriteId.ReplayRecording,
            CurrentReplayRecordingPhase.Succeeded or CurrentReplayRecordingPhase.Degraded =>
                BppDockButtonSpriteId.ReplayView,
            CurrentReplayRecordingPhase.Failed or CurrentReplayRecordingPhase.Unavailable =>
                BppDockButtonSpriteId.ReplayRetry,
            _ => BppDockButtonSpriteId.ReplayExport,
        };
}

internal sealed class CurrentReplayRecordingButtonHoverRelay
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
{
    private CurrentReplayRecordingButtonController? _owner;

    internal void Bind(CurrentReplayRecordingButtonController owner) => _owner = owner;

    public void OnPointerEnter(PointerEventData eventData) => _owner?.ShowTooltip();

    public void OnPointerExit(PointerEventData eventData) => _owner?.HideTooltip();
}
