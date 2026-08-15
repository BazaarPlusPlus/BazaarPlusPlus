#nullable enable
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using TheBazaar.Cues;
using TheBazaar.SequenceFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CurrentReplayRecordingButtonController : MonoBehaviour
{
    private const string CloneName = "BPP_CurrentReplayRecordingButton";

    // This is the blue hexagonal Cue sequence used by the main-menu Merchandise button.
    private const string MerchandiseCueAssetGuid = "8f41781147786fa4cb695e5768582615";
    private const float DockButtonGap = BppSettingsDockPlacement.DefaultSiblingGap;
    private const int ScreenResizeSyncFrameCount = 6;
    private const int LayoutImmediateSyncFrameCount = 2;
    private Button? _settingsButton;
    private Button? _nativeReplayButton;
    private Button? _nativeRecapButton;
    private Button? _nativeRecapBackButton;
    private Button? _button;
    private RectTransform? _cloneRect;
    private GameObject? _clone;
    private Image? _icon;
    private CurrentReplayRecordingCueActivator? _cueActivator;
    private BppDockButtonSpriteId? _lastSpriteId;
    private readonly BppDockButtonScreenLayout _screenLayout = new();
    private readonly CurrentReplayRecordingUiLogState _uiLogState = new();
    private readonly BppScreenResizeSyncTracker _screenResizeSync = new(ScreenResizeSyncFrameCount);
    private readonly BppDockLayoutSyncTracker _layoutSync = new(LayoutImmediateSyncFrameCount);
    private bool _layoutAvailable;
    private bool _anchorWasActive;
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

        _cueActivator = ConfigureMerchandiseCue(clone);
        ApplyIcon(CurrentReplayRecordingPhase.Ready);

        CombatReplayRuntime.Instance?.PrepareCurrentReplayRecordingAvailability();
        SyncLayout();
        Refresh();
    }

    private void LateUpdate()
    {
        // SyncLayout() sweeps every Button in the scene, so it is gated the same way the sibling
        // CollectionPanelDockButtonController gates its own placement sync. The anchor-activity
        // check is the extra trigger this controller needs: it docks against the collection button,
        // which screenshot suppression toggles inactive outside of any scene or resolution change.
        var anchorIsActive = IsDockAnchorActive();
        var shouldSync = _layoutSync.ShouldSync(
            SceneManager.GetActiveScene().name,
            Time.realtimeSinceStartup
        );
        shouldSync |= _screenResizeSync.ShouldSync(Screen.width, Screen.height);
        shouldSync |= anchorIsActive != _anchorWasActive;
        _anchorWasActive = anchorIsActive;
        if (shouldSync)
            SyncLayout();
        Refresh();
    }

    private bool IsDockAnchorActive()
    {
        if (_settingsButton == null)
            return false;

        var collectionRect = _settingsButton
            .GetComponent<CollectionPanelDockButtonController>()
            ?.DockButtonRect;
        return collectionRect != null && collectionRect.gameObject.activeInHierarchy;
    }

    private void OnDisable()
    {
        _cueActivator?.Hide();
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

    private static CurrentReplayRecordingCueActivator ConfigureMerchandiseCue(GameObject clone)
    {
        var cueActivator =
            clone.GetComponent<CurrentReplayRecordingCueActivator>()
            ?? clone.AddComponent<CurrentReplayRecordingCueActivator>();
        cueActivator._cuePopupRef = new AssetReferenceT<SequenceDataModel>(MerchandiseCueAssetGuid);
        return cueActivator;
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
        {
            _cueActivator?.Hide();
            return;
        }

        _button.interactable = snapshot.CanReveal || (nativeActionsBound && snapshot.CanStart);
        if (_cueActivator != null)
            _cueActivator.defaultValue = CurrentReplayRecordingText.Tooltip(snapshot);
        ApplyIcon(snapshot.Phase);
    }

    private void OnClicked()
    {
        var runtime = CombatReplayRuntime.Instance;
        if (runtime == null)
            return;

        var snapshot = runtime.GetCurrentReplayRecordingSnapshot();
        if (snapshot.CanReveal)
        {
            runtime.TryRevealCurrentReplayVideo(out _);
            Refresh();
            return;
        }

        var nativeReplayButton = _nativeReplayButton;
        var nativeRecapButton = _nativeRecapButton;
        var nativeRecapBackButton = _nativeRecapBackButton;
        if (
            nativeReplayButton == null
            || nativeRecapButton == null
            || nativeRecapBackButton == null
        )
            return;

        if (snapshot.CanStart)
            runtime.TryStartCurrentReplayRecording(
                nativeReplayButton.onClick.Invoke,
                nativeRecapButton.onClick.Invoke,
                nativeRecapBackButton.onClick.Invoke,
                out _
            );
        Refresh();
    }

    private static CurrentReplayRecordingSnapshot GetDisplaySnapshot() =>
        CombatReplayRuntime.Instance?.GetCurrentReplayRecordingSnapshot() ?? default;

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
            CurrentReplayRecordingPhase.Failed => BppDockButtonSpriteId.ReplayRetry,
            CurrentReplayRecordingPhase.Unavailable => BppDockButtonSpriteId.ReplayExport,
            _ => BppDockButtonSpriteId.ReplayExport,
        };
}

internal sealed class CurrentReplayRecordingCueActivator : UICueActivator
{
    private PositioningCondition.PositioningType? _originalPositioning;
    private PositioningCondition.RepositionBehavior? _originalRepositioningBehavior;

    public override void Show()
    {
        var positioning =
            cuePopup?.GetConditionRequiredToActivate<DynamicTransformPositioningCondition>();
        if (positioning != null)
        {
            _originalPositioning ??= positioning.Positioning;
            _originalRepositioningBehavior ??= positioning.RepositioningBehavior;
            positioning.Positioning = PositioningCondition.PositioningType.Top;
            // The Cue prefab's padded root is wider than its visible frame. Nudge moves that
            // entire root off-screen and counter-moves only the pointer, leaving a lone arrow.
            positioning.RepositioningBehavior = PositioningCondition.RepositionBehavior.None;
        }

        base.Show();
    }

    public override void Hide()
    {
        base.Hide();

        if (
            _originalPositioning is not { } originalPositioning
            || _originalRepositioningBehavior is not { } originalRepositioningBehavior
        )
            return;

        var positioning =
            cuePopup?.GetConditionRequiredToActivate<DynamicTransformPositioningCondition>();
        if (positioning != null)
        {
            positioning.Positioning = originalPositioning;
            positioning.RepositioningBehavior = originalRepositioningBehavior;
        }
        _originalPositioning = null;
        _originalRepositioningBehavior = null;
    }
}
