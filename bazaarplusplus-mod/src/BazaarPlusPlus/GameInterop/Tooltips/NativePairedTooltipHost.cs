#nullable enable
using TheBazaar;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.Tooltips;

// The native auxiliary controller is a game-wide singleton. This host serializes its own
// sessions, while each consumer must resolve conflicts with other native tooltip users.
internal sealed class NativePairedTooltipHost
{
    private NativePairedTooltipSession? _activeSession;

    // Release invalidates the generation before calling releaseOwnerResources. Cancel pending
    // work and drop the owner's references here, before the host destroys UI or restores it.
    internal NativePairedTooltipSession Acquire(object owner, Action releaseOwnerResources)
    {
        if (_activeSession != null && !_activeSession.HasOwner(owner))
        {
            _activeSession.Release(restoreNativeContent: false);
            _activeSession = null;
        }

        return _activeSession ??= new NativePairedTooltipSession(owner, releaseOwnerResources);
    }
}

internal sealed class NativePairedTooltipSession
{
    private readonly object _owner;
    private readonly Action _releaseOwnerResources;

    private AuxiliaryTooltipController? _activeAuxiliary;
    private CardTooltipController? _activePrimary;
    private NativeAuxiliaryHostState? _preparedNativeHost;
    private CanvasGroupGate? _preparedPrimaryGate;
    private CanvasGroupGate? _preparedAuxiliaryGate;
    private RectTransform? _nativeAuxParentRect;
    private VerticalLayoutGroup? _nativeAuxLayout;
    private int _nativeNormalBottomPadding;
    private GameObject? _contentRoot;
    private GameObject? _nativeBackgroundRoot;
    private Coroutine? _visibilityFade;
    private Action<float>? _onContentWidthChanged;
    private NativePairedTooltipOptions _options;
    private float _currentContentWidth;
    private float _frameHorizontalBleed;
    private bool _hidePending;
    private int _generation;

    internal NativePairedTooltipSession(object owner, Action releaseOwnerResources)
    {
        _owner = owner;
        _releaseOwnerResources = releaseOwnerResources;
    }

    // Owner work and host fades must share this generation so a racing continuation cannot
    // reattach after Release invalidates it.
    internal int Generation => _generation;

    internal bool HasOwner(object owner) => ReferenceEquals(_owner, owner);

    internal bool OwnsPrimary(CardTooltipController controller) =>
        ReferenceEquals(_activePrimary, controller);

    internal bool OwnsAuxiliary(AuxiliaryTooltipController controller) =>
        ReferenceEquals(_activeAuxiliary, controller);

    internal bool IsContentActive => _contentRoot?.activeInHierarchy == true;

    internal void PreparePrimary(CardTooltipController primary)
    {
        if (primary == null)
            return;
        if (
            _preparedPrimaryGate != null
            && ReferenceEquals(_preparedPrimaryGate.Controller, primary)
        )
            return;

        if (_activePrimary != null || _hidePending)
        {
            var displacedAuxiliary = _activeAuxiliary;
            ConcealNativeAuxiliary(displacedAuxiliary);
            Release(restoreNativeContent: false);
            if (displacedAuxiliary != null)
                Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
        }
        RestorePreparedPrimaryGate();
        // Tooltip_Main may be absent in a native prefab. Leave the primary visible in that case.
        var target = FindDescendant(primary.CanvasContentRectTransform, "Tooltip_Main");
        if (target != null)
            _preparedPrimaryGate = CanvasGroupGate.Create(primary, target.gameObject);
    }

    internal void CancelPreparedPrimary(CardTooltipController primary)
    {
        if (
            _preparedPrimaryGate == null
            || !ReferenceEquals(_preparedPrimaryGate.Controller, primary)
        )
            return;

        if (ReferenceEquals(_activePrimary, primary))
            Release(restoreNativeContent: false);
        else
            RestorePreparedPrimaryGate();
    }

    internal void PrepareAuxiliary(AuxiliaryTooltipController auxiliary)
    {
        if (_activeAuxiliary != null || _contentRoot != null)
            Release(restoreNativeContent: false);
        RestorePreparedNativeHost();
        // Prepare captures the frame before AssignTooltipFrame and Show captures it afterward.
        // Moving either capture across that call can restore the wrong tier sprite.
        _preparedNativeHost = NativeAuxiliaryHostState.Capture(auxiliary);
        // Gate auxParent because a root CanvasGroup cannot reach the nested Canvas.
        // Reuse a closed gate to avoid a one-frame flash while Unity defers destruction.
        if (
            _preparedAuxiliaryGate != null
            && ReferenceEquals(_preparedAuxiliaryGate.Controller, auxiliary)
        )
        {
            _preparedAuxiliaryGate.SetAlpha(0f);
        }
        else
        {
            RestorePreparedAuxiliaryGate();
            if (auxiliary.auxParent != null)
            {
                _preparedAuxiliaryGate = CanvasGroupGate.Create(
                    auxiliary,
                    auxiliary.auxParent.gameObject,
                    forceNonInteractive: true
                );
            }
        }
    }

    internal void CancelPreparedAuxiliary(AuxiliaryTooltipController auxiliary)
    {
        if (
            _preparedNativeHost == null
            || !ReferenceEquals(_preparedNativeHost.Controller, auxiliary)
        )
            return;

        // Keep the auxParent gate closed through native teardown because an in-flight native fade
        // can bring its own CanvasGroup back up.
        ConcealNativeAuxiliary(auxiliary);
        if (ReferenceEquals(_activeAuxiliary, auxiliary))
            Release(restoreNativeContent: false);
        else
            RestorePreparedNativeHost(restoreContentVisibility: false);
    }

    internal bool ReleaseForNativeShow(AuxiliaryTooltipController controller)
    {
        var displacedActivePresentation = OwnsAuxiliary(controller);
        if (displacedActivePresentation)
            Release(restoreNativeContent: false);

        RestorePreparedNativeHostForNativeShow(controller);
        RestorePreparedAuxiliaryGate(expectedController: controller);
        return displacedActivePresentation;
    }

    // The native fade can restore its own CanvasGroup's alpha. Gate auxParent below it
    // to keep the empty frame concealed.
    internal bool TryConcealVisibleEmptyNativeAuxiliary(AuxiliaryTooltipController auxiliary)
    {
        if (auxiliary == null || auxiliary.auxParent == null)
            return false;

        if (
            _preparedAuxiliaryGate != null
            && ReferenceEquals(_preparedAuxiliaryGate.Controller, auxiliary)
        )
        {
            _preparedAuxiliaryGate.SetAlpha(0f);
            return _preparedAuxiliaryGate.Alpha <= NativePairedTooltipMetrics.Epsilon;
        }

        RestorePreparedAuxiliaryGate();
        _preparedAuxiliaryGate = CanvasGroupGate.Create(
            auxiliary,
            auxiliary.auxParent.gameObject,
            forceNonInteractive: true
        );
        return _preparedAuxiliaryGate != null
            && _preparedAuxiliaryGate.Alpha <= NativePairedTooltipMetrics.Epsilon;
    }

    internal bool TryOpen(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        NativePairedTooltipOptions options,
        out NativePairedTooltipOpenFailure failure
    )
    {
        failure = NativePairedTooltipOpenFailure.None;
        if (
            auxiliary.auxParent == null
            || auxiliary.headerText == null
            || auxiliary.bodyText == null
        )
        {
            failure = NativePairedTooltipOpenFailure.MissingAuxiliaryFields;
            return false;
        }

        if (_activeAuxiliary != null || _contentRoot != null)
            Release(restoreNativeContent: false);

        _options = options;
        if (
            _preparedNativeHost == null
            || !ReferenceEquals(_preparedNativeHost.Controller, auxiliary)
        )
        {
            _preparedNativeHost = NativeAuxiliaryHostState.Capture(auxiliary);
        }

        if (
            _preparedPrimaryGate == null
            || !ReferenceEquals(_preparedPrimaryGate.Controller, primary)
        )
            PreparePrimary(primary);
        if (
            _preparedAuxiliaryGate == null
            || !ReferenceEquals(_preparedAuxiliaryGate.Controller, auxiliary)
        )
        {
            _preparedAuxiliaryGate = CanvasGroupGate.Create(
                auxiliary,
                auxiliary.auxParent.gameObject,
                forceNonInteractive: true
            );
        }
        if (_preparedAuxiliaryGate == null)
        {
            // Conceal the native canvas group before Release restores its gates, or the active
            // header can flash on its own.
            ConcealNativeAuxiliary(auxiliary);
            Release(restoreNativeContent: false);
            failure = NativePairedTooltipOpenFailure.DyingController;
            return false;
        }

        _generation++;
        _hidePending = false;
        _activeAuxiliary = auxiliary;
        _activePrimary = primary;
        auxiliary.headerText.gameObject.SetActive(false);
        auxiliary.bodyText.gameObject.SetActive(false);
        auxiliary.dividerParent?.SetActive(false);
        if (
            auxiliary.backgroundImage == null
            || primary.backgroundImage == null
            || primary.backgroundImage.sprite == null
        )
        {
            ConcealNativeAuxiliary(auxiliary);
            Release(restoreNativeContent: false);
            failure = NativePairedTooltipOpenFailure.MissingBackground;
            return false;
        }

        auxiliary.backgroundImage.sprite = primary.backgroundImage.sprite;
        auxiliary.backgroundImage.enabled = primary.backgroundImage.enabled;
        PrepareNativePresentation(auxiliary);
        ApplyContentWidth(auxiliary, options.PreferredContentWidth);
        if (!TryCreateNativeBackground(auxiliary, primary))
        {
            ConcealNativeAuxiliary(auxiliary);
            Release(restoreNativeContent: false);
            failure = NativePairedTooltipOpenFailure.BackgroundCloneRejected;
            return false;
        }

        return true;
    }

    // The host owns contentRoot after attachment. Position settles its layout at the final
    // width while the pair is still concealed.
    internal bool AttachContent(GameObject contentRoot, Action<float>? onContentWidthChanged)
    {
        var auxiliary = _activeAuxiliary;
        if (auxiliary == null || contentRoot == null)
            return false;

        _contentRoot = contentRoot;
        _onContentWidthChanged = onContentWidthChanged;
        ApplyContentWidth(auxiliary, _options.PreferredContentWidth);
        return true;
    }

    // Call after TryOpen to avoid building rows that the canvas height budget will hide.
    internal bool TryMeasureContentHeightLimit(out float contentHeightLimit)
    {
        contentHeightLimit = 0f;
        if (
            _activeAuxiliary == null
            || _activePrimary == null
            || _activePrimary.RootCanvasComponent == null
            || _activePrimary.RootCanvasComponent.transform is not RectTransform canvasRect
        )
            return false;

        contentHeightLimit = NativePairedTooltipPlacementMath.ContentHeightLimit(
            canvasRect.rect.height,
            _options.CanvasMargin,
            CanvasUnitsPerLocalUnit(_activeAuxiliary.PositioningRectTransform, canvasRect)
        );
        return true;
    }

    internal PlacementResult Position(Transform anchor, IPairedContentBudget content)
    {
        var auxiliary = _activeAuxiliary;
        var primary = _activePrimary;
        if (
            auxiliary == null
            || primary == null
            || _contentRoot == null
            || primary.RootCanvasComponent == null
        )
            return PlacementResult.Unplaced;

        // The primary's bounds determine the final width. Rebuilding the auxiliary now would
        // measure it at a stale width.
        Canvas.ForceUpdateCanvases();
        TempoUIUtility.ForceRebuildRecursive(primary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(primary.PositioningRectTransform);

        if (primary.RootCanvasComponent.transform is not RectTransform canvasRect)
            return PlacementResult.Unplaced;

        var canvasBounds = NativePairedTooltipPlacementMath.Inset(
            canvasRect.rect,
            _options.CanvasMargin
        );
        var primaryBounds = GetPrimaryVisibleBounds(primary, canvasRect);
        var availableRight = NativePairedTooltipPlacementMath.AvailableRight(
            canvasBounds,
            primaryBounds,
            _options.Gap
        );
        var availableLeft = NativePairedTooltipPlacementMath.AvailableLeft(
            canvasBounds,
            primaryBounds,
            _options.Gap
        );
        var canvasUnitsPerLocalUnit = CanvasUnitsPerLocalUnit(
            auxiliary.PositioningRectTransform,
            canvasRect
        );
        var preferredFrameWidth =
            (_options.PreferredContentWidth + _frameHorizontalBleed) * canvasUnitsPerLocalUnit;
        var side = NativePairedTooltipPlacementMath.ChooseSide(
            availableRight,
            availableLeft,
            preferredFrameWidth
        );

        ApplyContentWidth(
            auxiliary,
            NativePairedTooltipPlacementMath.ResolveContentWidth(
                side,
                availableRight,
                availableLeft,
                canvasUnitsPerLocalUnit,
                _frameHorizontalBleed,
                _options.PreferredContentWidth
            )
        );
        SetNativeBottomPadding(_nativeNormalBottomPadding);
        content.RestoreAll();
        // Restoring a previously trimmed perspective can reactivate nested ContentSizeFitters.
        // Settle the complete tree once at its final width before deriving the trim budget.
        ForceRebuildLayout(auxiliary);
        ApplyNativeHeight(auxiliary);
        FitContentToCanvas(auxiliary, canvasRect, canvasBounds, content);

        var panelRect = auxiliary.backgroundImage.rectTransform;
        var panelBounds = GetCanvasLocalBounds(panelRect, canvasRect);
        var offset = NativePairedTooltipPlacementMath.ResolvePairOffset(
            side,
            primaryBounds,
            panelBounds,
            _options.Gap
        );
        TranslateRect(auxiliary.PositioningRectTransform, canvasRect, offset);

        panelBounds = GetCanvasLocalBounds(panelRect, canvasRect);
        var verticalAdjustment = NativePairedTooltipPlacementMath.ResolveVerticalAdjustment(
            panelBounds,
            canvasBounds
        );
        var topAlignmentAdjusted =
            Mathf.Abs(verticalAdjustment) > NativePairedTooltipMetrics.Epsilon;
        if (!Mathf.Approximately(verticalAdjustment, 0f))
        {
            TranslateRect(
                auxiliary.PositioningRectTransform,
                canvasRect,
                new Vector2(0f, verticalAdjustment)
            );
            panelBounds = GetCanvasLocalBounds(panelRect, canvasRect);
        }

        return new PlacementResult(
            positioned: true,
            side: side,
            overflowed: NativePairedTooltipPlacementMath.Overflows(
                side,
                primaryBounds,
                panelBounds,
                canvasRect.rect,
                _options.Gap
            ),
            widthBelowReadable: _currentContentWidth < _options.ReadableContentWidth,
            topAlignmentAdjusted: topAlignmentAdjusted
        );
    }

    // Mask intermediate ContentSizeFitter passes so a swap cannot flash unsettled geometry.
    internal IDisposable BeginMaskedLayout() => new MaskedLayoutScope(this);

    internal void Reveal()
    {
        if (_activeAuxiliary == null || _activePrimary == null || _contentRoot == null)
            return;

        _hidePending = false;
        StartVisibilityFade(targetAlpha: 1f, cleanupOnComplete: false);
    }

    internal void Hide()
    {
        if (
            _activeAuxiliary == null
            && _contentRoot == null
            && _preparedPrimaryGate == null
            && _preparedAuxiliaryGate == null
        )
            return;

        if (_hidePending)
            return;
        _hidePending = true;
        if (_activePrimary != null)
            _activePrimary.SetLockedFlag(false);

        var visibleAlpha = Mathf.Max(
            _preparedPrimaryGate?.Alpha ?? 0f,
            _preparedAuxiliaryGate?.Alpha ?? 0f
        );
        if (visibleAlpha <= NativePairedTooltipMetrics.Epsilon || _activeAuxiliary == null)
        {
            CompleteAnimatedHide(_generation);
            return;
        }

        StartVisibilityFade(targetAlpha: 0f, cleanupOnComplete: true);
    }

    // Call from the native dismissal signal because deactivating the controller can kill
    // its fade coroutine without running the completion callback.
    internal void ForceSettle()
    {
        // The native side is already tearing down. Completing its hide here would re-enter it.
        ConcealNativeAuxiliary(_activeAuxiliary);
        Release(restoreNativeContent: false);
    }

    private void StartVisibilityFade(float targetAlpha, bool cleanupOnComplete)
    {
        StopVisibilityFade();
        var auxiliary = _activeAuxiliary;
        if (auxiliary == null || !auxiliary.isActiveAndEnabled)
        {
            SetVisibilityAlpha(targetAlpha);
            if (cleanupOnComplete)
                CompleteAnimatedHide(_generation);
            return;
        }

        var generation = _generation;
        _visibilityFade = auxiliary.StartCoroutine(
            FadeVisibility(targetAlpha, cleanupOnComplete, generation)
        );
    }

    private System.Collections.IEnumerator FadeVisibility(
        float targetAlpha,
        bool cleanupOnComplete,
        int generation
    )
    {
        var primaryStart = _preparedPrimaryGate?.Alpha ?? targetAlpha;
        var auxiliaryStart = _preparedAuxiliaryGate?.Alpha ?? targetAlpha;
        var elapsed = 0f;
        while (elapsed < _options.FadeDuration)
        {
            if (generation != _generation)
            {
                _visibilityFade = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / _options.FadeDuration);
            if (!cleanupOnComplete)
            {
                _preparedPrimaryGate?.SetAlpha(Mathf.Lerp(primaryStart, targetAlpha, progress));
            }
            _preparedAuxiliaryGate?.SetAlpha(Mathf.Lerp(auxiliaryStart, targetAlpha, progress));
            yield return null;
        }

        if (cleanupOnComplete)
        {
            _preparedPrimaryGate?.SetAlpha(0f);
            _preparedAuxiliaryGate?.SetAlpha(0f);
        }
        else
        {
            SetVisibilityAlpha(targetAlpha);
        }
        _visibilityFade = null;
        if (cleanupOnComplete)
            CompleteAnimatedHide(generation);
    }

    private void SetVisibilityAlpha(float alpha)
    {
        _preparedPrimaryGate?.SetAlpha(alpha);
        _preparedAuxiliaryGate?.SetAlpha(alpha);
    }

    private void StopVisibilityFade()
    {
        if (_visibilityFade == null)
            return;

        if (_activeAuxiliary != null)
            _activeAuxiliary.StopCoroutine(_visibilityFade);
        _visibilityFade = null;
    }

    private void CompleteAnimatedHide(int generation)
    {
        if (generation != _generation)
            return;

        ConcealNativeAuxiliary(_activeAuxiliary);
        _visibilityFade = null;
        if (!Release(restoreNativeContent: false))
            return;
        Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
    }

    private static void ConcealNativeAuxiliary(AuxiliaryTooltipController? auxiliary)
    {
        if (auxiliary?.tooltipCanvasGroup != null)
            auxiliary.tooltipCanvasGroup.alpha = 0f;
    }

    // Release can hand the native host back in a later call. Keep the gate closed while the
    // native fade can expose an empty shell. Invalidate the generation before owner cleanup.
    internal bool Release(bool restoreNativeContent)
    {
        var hadActiveContent =
            _activeAuxiliary != null
            || _contentRoot != null
            || _nativeBackgroundRoot != null
            || _preparedNativeHost != null
            || _preparedPrimaryGate != null
            || _preparedAuxiliaryGate != null;
        if (!hadActiveContent)
            return false;

        StopVisibilityFade();
        _generation++;
        _releaseOwnerResources();
        if (_contentRoot != null)
        {
            _contentRoot.SetActive(false);
            Object.Destroy(_contentRoot);
        }
        if (_nativeBackgroundRoot != null)
        {
            _nativeBackgroundRoot.SetActive(false);
            Object.Destroy(_nativeBackgroundRoot);
        }
        RestorePreparedNativeHost(restoreNativeContent);
        if (restoreNativeContent)
            RestorePreparedAuxiliaryGate();
        else
            _preparedAuxiliaryGate?.SetAlpha(0f);
        RestorePreparedPrimaryGate();
        if (_activePrimary != null)
            _activePrimary.SetLockedFlag(false);

        _activeAuxiliary = null;
        _activePrimary = null;
        _contentRoot = null;
        _nativeBackgroundRoot = null;
        _nativeAuxParentRect = null;
        _nativeAuxLayout = null;
        _nativeNormalBottomPadding = 0;
        _onContentWidthChanged = null;
        _currentContentWidth = _options.PreferredContentWidth;
        _frameHorizontalBleed = 0f;
        _hidePending = false;
        return true;
    }

    private void RestorePreparedNativeHost(bool restoreContentVisibility = true)
    {
        if (_preparedNativeHost == null)
            return;

        _preparedNativeHost.Restore(restoreContentVisibility);
        if (restoreContentVisibility)
            _preparedNativeHost = null;
    }

    private void RestorePreparedNativeHostForNativeShow(
        AuxiliaryTooltipController expectedController
    )
    {
        if (
            _preparedNativeHost == null
            || !ReferenceEquals(_preparedNativeHost.Controller, expectedController)
        )
            return;

        _preparedNativeHost.RestoreForNativeShow();
        _preparedNativeHost = null;
    }

    private void RestorePreparedAuxiliaryGate(AuxiliaryTooltipController? expectedController = null)
    {
        if (
            expectedController != null
            && _preparedAuxiliaryGate != null
            && !ReferenceEquals(_preparedAuxiliaryGate.Controller, expectedController)
        )
            return;
        _preparedAuxiliaryGate?.Restore();
        _preparedAuxiliaryGate = null;
    }

    private void RestorePreparedPrimaryGate()
    {
        _preparedPrimaryGate?.Restore();
        _preparedPrimaryGate = null;
    }

    private void PrepareNativePresentation(AuxiliaryTooltipController auxiliary)
    {
        _nativeAuxParentRect = auxiliary.auxParent.transform as RectTransform;
        if (_nativeAuxParentRect == null)
            return;

        var frameRect = auxiliary.backgroundImage.rectTransform;
        _frameHorizontalBleed = Mathf.Max(
            0f,
            frameRect.rect.width - _nativeAuxParentRect.rect.width
        );
        _nativeAuxParentRect.anchorMin = new Vector2(0.5f, 0.5f);
        _nativeAuxParentRect.anchorMax = new Vector2(0.5f, 0.5f);
        _nativeAuxParentRect.pivot = new Vector2(0.5f, 0.5f);
        // Match the sibling frame's center because mirroring its serialized offset doubles the inset.
        _nativeAuxParentRect.anchoredPosition = frameRect.anchoredPosition;

        _nativeAuxLayout = auxiliary.auxParent.GetComponent<VerticalLayoutGroup>();
        if (_nativeAuxLayout != null)
        {
            var padding = _nativeAuxLayout.padding;
            _nativeNormalBottomPadding = Mathf.Max(
                0,
                padding.bottom - _options.NativeBottomPaddingReduction
            );
            _nativeAuxLayout.padding = new RectOffset(
                padding.left,
                padding.right,
                padding.top,
                _nativeNormalBottomPadding
            );
        }
    }

    private void ApplyContentWidth(AuxiliaryTooltipController auxiliary, float contentWidth)
    {
        _currentContentWidth = Mathf.Max(1f, contentWidth);
        if (_contentRoot != null)
        {
            var contentRect = (RectTransform)_contentRoot.transform;
            var contentLayout = _contentRoot.GetComponent<LayoutElement>();
            contentLayout.preferredWidth = _currentContentWidth;
            contentLayout.minWidth = _currentContentWidth;
            contentRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                _currentContentWidth
            );
        }

        _onContentWidthChanged?.Invoke(_currentContentWidth);

        _nativeAuxParentRect?.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            _currentContentWidth
        );
        auxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            _currentContentWidth + _frameHorizontalBleed
        );
    }

    private void ApplyNativeHeight(AuxiliaryTooltipController auxiliary)
    {
        var contentHeight = _nativeAuxParentRect?.rect.height ?? 0f;
        if (contentHeight <= 0f)
            return;

        auxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            contentHeight
        );
    }

    private void FitContentToCanvas(
        AuxiliaryTooltipController auxiliary,
        RectTransform canvasRect,
        Rect canvasBounds,
        IPairedContentBudget content
    )
    {
        var canvasUnitsPerLocalUnit = CanvasUnitsPerLocalUnit(
            auxiliary.PositioningRectTransform,
            canvasRect
        );
        var panelHeight = GetCanvasLocalBounds(
            auxiliary.backgroundImage.rectTransform,
            canvasRect
        ).height;
        var reclaimedBottomPadding = CompactNativeBottomPaddingIfDense(
            panelHeight,
            canvasBounds.height,
            canvasUnitsPerLocalUnit
        );
        var effectivePanelHeight = panelHeight - reclaimedBottomPadding * canvasUnitsPerLocalUnit;
        var requiredHeightReduction = Mathf.Max(
            0f,
            (effectivePanelHeight - canvasBounds.height - NativePairedTooltipMetrics.Epsilon)
                / canvasUnitsPerLocalUnit
        );
        var contentChanged = content.ApplyHeightReduction(requiredHeightReduction);
        if (reclaimedBottomPadding <= 0f && !contentChanged)
            return;

        RebuildAfterHeightBudgetChange(auxiliary);
    }

    private float CompactNativeBottomPaddingIfDense(
        float panelHeight,
        float availableHeight,
        float canvasUnitsPerLocalUnit
    )
    {
        var denseBottomPadding = Mathf.Min(
            _nativeNormalBottomPadding,
            _options.NativeDenseBottomPaddingMaximum
        );
        if (
            !NativePairedTooltipPlacementMath.ShouldUseDenseBottomPadding(
                panelHeight,
                availableHeight,
                _nativeNormalBottomPadding * canvasUnitsPerLocalUnit,
                denseBottomPadding * canvasUnitsPerLocalUnit
            )
        )
            return 0f;

        SetNativeBottomPadding(denseBottomPadding);
        return Mathf.Max(0, _nativeNormalBottomPadding - denseBottomPadding);
    }

    private void SetNativeBottomPadding(int bottomPadding)
    {
        if (_nativeAuxLayout == null)
            return;

        var padding = _nativeAuxLayout.padding;
        if (padding.bottom == bottomPadding)
            return;
        _nativeAuxLayout.padding = new RectOffset(
            padding.left,
            padding.right,
            padding.top,
            bottomPadding
        );
    }

    private void RebuildAfterHeightBudgetChange(AuxiliaryTooltipController auxiliary)
    {
        // The visibility plan already used settled geometry, so one final pass is enough here.
        Canvas.ForceUpdateCanvases();
        TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(auxiliary.PositioningRectTransform);
        ApplyNativeHeight(auxiliary);
    }

    private static void ForceRebuildLayout(AuxiliaryTooltipController auxiliary)
    {
        // A newly activated nested ContentSizeFitter can expose its previous preferred height on
        // the first pass. Two bounded passes resolve the child and parent sizes in this frame.
        for (var pass = 0; pass < 2; pass++)
        {
            Canvas.ForceUpdateCanvases();
            TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(auxiliary.PositioningRectTransform);
        }
    }

    private bool TryCreateNativeBackground(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary
    )
    {
        var sourceContainer = primary.gradientImage?.transform.parent as RectTransform;
        var sourceMaskImage = sourceContainer?.GetComponent<Image>();
        var sourceMask = sourceContainer?.GetComponent<Mask>();
        var frameRect = auxiliary.backgroundImage?.rectTransform;
        if (
            sourceContainer == null
            || sourceMaskImage == null
            || sourceMask == null
            || frameRect == null
            || frameRect.parent == null
        )
            return false;

        var background = new GameObject(
            "BppPairedTooltipNativeBackground",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Mask),
            typeof(LayoutElement)
        );
        var backgroundRect = (RectTransform)background.transform;
        backgroundRect.SetParent(frameRect.parent, worldPositionStays: false);
        CopyRectTransform(frameRect, backgroundRect);
        backgroundRect.SetSiblingIndex(frameRect.GetSiblingIndex());
        background.GetComponent<LayoutElement>().ignoreLayout = true;
        CopyImage(sourceMaskImage, background.GetComponent<Image>());
        background.GetComponent<Image>().enabled = true;
        var mask = background.GetComponent<Mask>();
        mask.enabled = sourceMask.enabled;
        mask.showMaskGraphic = sourceMask.showMaskGraphic;

        for (var childIndex = 0; childIndex < sourceContainer.childCount; childIndex++)
        {
            if (
                sourceContainer.GetChild(childIndex) is not RectTransform sourceRect
                || !sourceRect.TryGetComponent<Image>(out var sourceImage)
            )
                continue;

            var layer = new GameObject(
                $"BppPairedTooltipNativeLayer_{childIndex}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(backgroundRect, worldPositionStays: false);
            CopyRectTransform(sourceRect, layerRect);
            CopyImage(sourceImage, layer.GetComponent<Image>());
            layer.SetActive(sourceRect.gameObject.activeSelf);
        }

        _nativeBackgroundRoot = background;
        return true;
    }

    private static void CopyRectTransform(RectTransform source, RectTransform destination)
    {
        destination.anchorMin = source.anchorMin;
        destination.anchorMax = source.anchorMax;
        destination.pivot = source.pivot;
        destination.sizeDelta = source.sizeDelta;
        destination.anchoredPosition3D = source.anchoredPosition3D;
        destination.localRotation = source.localRotation;
        destination.localScale = source.localScale;
    }

    private static void CopyImage(Image source, Image destination)
    {
        destination.sprite = source.sprite;
        destination.material = source.material;
        destination.color = source.color;
        destination.type = source.type;
        destination.fillCenter = source.fillCenter;
        destination.fillMethod = source.fillMethod;
        destination.fillAmount = source.fillAmount;
        destination.fillClockwise = source.fillClockwise;
        destination.fillOrigin = source.fillOrigin;
        destination.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        destination.preserveAspect = source.preserveAspect;
        destination.useSpriteMesh = source.useSpriteMesh;
        destination.maskable = source.maskable;
        destination.raycastTarget = false;
        destination.enabled = source.enabled;
    }

    private readonly Vector3[] _worldCorners = new Vector3[4];

    private Rect GetCanvasLocalBounds(RectTransform rect, RectTransform canvasRect)
    {
        rect.GetWorldCorners(_worldCorners);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var corner in _worldCorners)
        {
            var local = canvasRect.InverseTransformPoint(corner);
            minX = Mathf.Min(minX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxX = Mathf.Max(maxX, local.x);
            maxY = Mathf.Max(maxY, local.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private Rect GetPrimaryVisibleBounds(CardTooltipController primary, RectTransform canvasRect)
    {
        var bounds = GetPrimaryFrameBounds(primary, canvasRect);
        if (
            primary.cooldownClock != null
            && primary.cooldownClock.gameObject.activeInHierarchy
            && primary.cooldownClock.transform is RectTransform cooldownRect
        )
        {
            bounds = NativePairedTooltipPlacementMath.Union(
                bounds,
                GetCanvasLocalBounds(cooldownRect, canvasRect)
            );
        }
        return bounds;
    }

    private Rect GetPrimaryFrameBounds(CardTooltipController primary, RectTransform canvasRect)
    {
        var bounds = GetCanvasLocalBounds(primary.PositioningRectTransform, canvasRect);
        if (primary.CanvasContentRectTransform != null)
        {
            bounds = NativePairedTooltipPlacementMath.Union(
                bounds,
                GetCanvasLocalBounds(primary.CanvasContentRectTransform, canvasRect)
            );
        }
        return bounds;
    }

    private static float CanvasUnitsPerLocalUnit(RectTransform rect, RectTransform canvasRect)
    {
        var origin = canvasRect.InverseTransformPoint(rect.TransformPoint(Vector3.zero));
        var horizontalUnit = canvasRect.InverseTransformPoint(rect.TransformPoint(Vector3.right));
        return Mathf.Max(
            0.0001f,
            Vector2.Distance(
                new Vector2(origin.x, origin.y),
                new Vector2(horizontalUnit.x, horizontalUnit.y)
            )
        );
    }

    private static void TranslateRect(
        RectTransform rect,
        RectTransform canvasRect,
        Vector2 canvasLocalDelta
    )
    {
        if (canvasLocalDelta == Vector2.zero)
            return;

        rect.position += canvasRect.TransformVector(
            new Vector3(canvasLocalDelta.x, canvasLocalDelta.y)
        );
    }

    internal static RectTransform? FindDescendant(Transform? root, string childName)
    {
        if (root == null)
            return null;
        for (var index = 0; index < root.childCount; index++)
        {
            var child = root.GetChild(index);
            if (
                string.Equals(child.name, childName, StringComparison.Ordinal)
                && child is RectTransform matching
            )
                return matching;
            var nested = FindDescendant(child, childName);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private sealed class MaskedLayoutScope : IDisposable
    {
        private readonly NativePairedTooltipSession _session;
        private readonly float _visibleAlpha;
        private bool _disposed;

        internal MaskedLayoutScope(NativePairedTooltipSession session)
        {
            _session = session;
            _visibleAlpha = session._preparedAuxiliaryGate?.Alpha ?? 1f;
            session._preparedAuxiliaryGate?.SetAlpha(0f);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _session._preparedAuxiliaryGate?.SetAlpha(_visibleAlpha);
        }
    }

    private sealed class CanvasGroupGate
    {
        private readonly CanvasGroup _group;
        private readonly bool _ownedGroup;
        private readonly float _originalAlpha;
        private readonly bool _originalInteractable;
        private readonly bool _originalBlocksRaycasts;
        private readonly bool _originalIgnoreParentGroups;
        private readonly bool _forceNonInteractive;
        private bool _restored;

        private CanvasGroupGate(
            object controller,
            CanvasGroup group,
            bool forceNonInteractive,
            bool ownedGroup
        )
        {
            Controller = controller;
            _forceNonInteractive = forceNonInteractive;
            _ownedGroup = ownedGroup;
            _group = group;
            _originalAlpha = _group.alpha;
            _originalInteractable = _group.interactable;
            _originalBlocksRaycasts = _group.blocksRaycasts;
            _originalIgnoreParentGroups = _group.ignoreParentGroups;
            SetAlpha(0f);
        }

        internal object Controller { get; }

        internal float Alpha => _group == null ? 0f : _group.alpha;

        internal static CanvasGroupGate? Create(
            object controller,
            GameObject target,
            bool forceNonInteractive = false
        )
        {
            var ownedGroup = false;
            var group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
                ownedGroup = true;
            }
            // AddComponent can return a dead reference on a target awaiting deferred destruction,
            // even while the target still passes other liveness checks.
            if (group == null)
                return null;
            return new CanvasGroupGate(controller, group, forceNonInteractive, ownedGroup);
        }

        internal void SetAlpha(float alpha)
        {
            if (_group == null || _restored)
                return;

            _group.alpha = Mathf.Clamp01(alpha);
            var interactive = _group.alpha >= 1f - NativePairedTooltipMetrics.Epsilon;
            _group.interactable = !_forceNonInteractive && interactive && _originalInteractable;
            _group.blocksRaycasts = !_forceNonInteractive && interactive && _originalBlocksRaycasts;
        }

        internal void Restore()
        {
            if (_restored || _group == null)
                return;

            _restored = true;
            _group.alpha = _originalAlpha;
            _group.interactable = _originalInteractable;
            _group.blocksRaycasts = _originalBlocksRaycasts;
            _group.ignoreParentGroups = _originalIgnoreParentGroups;
            // A deferred Destroy leaves a gate that same-frame Prepare can adopt and then lose
            // at frame end, flashing the shell.
            if (_ownedGroup)
                Object.DestroyImmediate(_group);
        }
    }

    private sealed class NativeAuxiliaryHostState
    {
        private readonly RectTransform? _auxParentRect;
        private readonly Vector2 _auxParentSizeDelta;
        private readonly Vector2 _auxParentAnchorMin;
        private readonly Vector2 _auxParentAnchorMax;
        private readonly Vector2 _auxParentPivot;
        private readonly Vector3 _auxParentAnchoredPosition;
        private readonly Vector2 _positioningSizeDelta;
        private readonly VerticalLayoutGroup? _layout;
        private readonly RectOffset? _padding;
        private readonly float _spacing;
        private readonly bool _headerWasActive;
        private readonly bool _bodyWasActive;
        private readonly bool _dividerWasActive;
        private readonly Sprite? _backgroundSprite;
        private readonly bool _backgroundImageWasEnabled;

        private NativeAuxiliaryHostState(AuxiliaryTooltipController controller)
        {
            Controller = controller;
            _auxParentRect = controller.auxParent?.transform as RectTransform;
            _auxParentSizeDelta = _auxParentRect?.sizeDelta ?? Vector2.zero;
            _auxParentAnchorMin = _auxParentRect?.anchorMin ?? Vector2.zero;
            _auxParentAnchorMax = _auxParentRect?.anchorMax ?? Vector2.zero;
            _auxParentPivot = _auxParentRect?.pivot ?? Vector2.zero;
            _auxParentAnchoredPosition = _auxParentRect?.anchoredPosition3D ?? Vector3.zero;
            _positioningSizeDelta = controller.PositioningRectTransform.sizeDelta;
            _layout = controller.auxParent?.GetComponent<VerticalLayoutGroup>();
            _padding =
                _layout == null
                    ? null
                    : new RectOffset(
                        _layout.padding.left,
                        _layout.padding.right,
                        _layout.padding.top,
                        _layout.padding.bottom
                    );
            _spacing = _layout?.spacing ?? 0f;
            _headerWasActive =
                controller.headerText != null && controller.headerText.gameObject.activeSelf;
            _bodyWasActive =
                controller.bodyText != null && controller.bodyText.gameObject.activeSelf;
            _dividerWasActive =
                controller.dividerParent != null && controller.dividerParent.activeSelf;
            _backgroundSprite = controller.backgroundImage?.sprite;
            _backgroundImageWasEnabled =
                controller.backgroundImage != null && controller.backgroundImage.enabled;
        }

        internal AuxiliaryTooltipController Controller { get; }

        internal static NativeAuxiliaryHostState Capture(AuxiliaryTooltipController controller) =>
            new(controller);

        internal void Restore(bool restoreContentVisibility)
        {
            if (Controller == null)
                return;

            Controller.PositioningRectTransform.sizeDelta = _positioningSizeDelta;
            if (_auxParentRect != null)
            {
                _auxParentRect.anchorMin = _auxParentAnchorMin;
                _auxParentRect.anchorMax = _auxParentAnchorMax;
                _auxParentRect.pivot = _auxParentPivot;
                _auxParentRect.anchoredPosition3D = _auxParentAnchoredPosition;
                _auxParentRect.sizeDelta = _auxParentSizeDelta;
            }
            if (_layout != null && _padding != null)
            {
                _layout.padding = new RectOffset(
                    _padding.left,
                    _padding.right,
                    _padding.top,
                    _padding.bottom
                );
                _layout.spacing = _spacing;
            }
            if (restoreContentVisibility)
            {
                if (Controller.headerText != null)
                    Controller.headerText.gameObject.SetActive(_headerWasActive);
                if (Controller.bodyText != null)
                    Controller.bodyText.gameObject.SetActive(_bodyWasActive);
                if (Controller.dividerParent != null)
                    Controller.dividerParent.SetActive(_dividerWasActive);
            }
            if (Controller.backgroundImage != null)
            {
                Controller.backgroundImage.sprite = _backgroundSprite;
                Controller.backgroundImage.enabled = _backgroundImageWasEnabled;
            }
        }

        internal void RestoreForNativeShow()
        {
            // Native Show assigns strings without reactivating header/body. A pooled controller
            // can otherwise retain inactive text and render an empty frame.
            Restore(restoreContentVisibility: false);
            if (Controller.headerText != null)
                Controller.headerText.gameObject.SetActive(true);
            if (Controller.bodyText != null)
                Controller.bodyText.gameObject.SetActive(true);
        }
    }
}
