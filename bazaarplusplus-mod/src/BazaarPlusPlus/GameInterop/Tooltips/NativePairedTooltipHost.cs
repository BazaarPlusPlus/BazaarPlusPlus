#nullable enable
using TheBazaar;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.Tooltips;

/// <summary>
/// Plugin-lifetime owner of the "custom content inside the native auxiliary tooltip, pinned beside
/// the native card tooltip" presentation.
/// </summary>
/// <remarks>
/// <para>
/// The host holds at most one active <see cref="NativePairedTooltipSession"/>. It is deliberately
/// <b>not</b> <see cref="IDisposable"/>: composition does not dispose GameInterop hosts (the native
/// card-preview host is the precedent), and the releasable unit here is the session, not the host.
/// Once a session is released the host keeps no Unity references, so it cannot carry a destroyed
/// <see cref="AuxiliaryTooltipController"/> across a scene load.
/// </para>
/// <para>
/// The host does not observe native tooltip events and does not arbitrate between features
/// competing for the native singleton. It can only answer "is this controller mine"
/// (<see cref="NativePairedTooltipSession.OwnsPrimary"/> /
/// <see cref="NativePairedTooltipSession.OwnsAuxiliary"/>); the decision of what to do about a
/// conflict — and any logging about it — belongs to the consuming feature's patch chain. Note that
/// the native auxiliary controller is a game-wide singleton that other code can drive directly
/// without going through this host, so a single active session serializes this host's own usage
/// only.
/// </para>
/// </remarks>
internal sealed class NativePairedTooltipHost
{
    private NativePairedTooltipSession? _activeSession;

    /// <summary>
    /// Acquires the single active session. An existing session belonging to another owner is
    /// released first.
    /// </summary>
    /// <param name="owner">Identity used to detect re-entrant acquisition.</param>
    /// <param name="releaseOwnerResources">
    /// Invoked in the middle of <see cref="NativePairedTooltipSession.Release"/>, after the host has
    /// stopped the fade and invalidated the generation but before it destroys UI or restores native
    /// state. The owner must cancel in-flight async work and drop its own references here.
    /// </param>
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

/// <summary>
/// One feature's hold on the paired native tooltips.
/// </summary>
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

    /// <summary>
    /// Monotonic identity of the current presentation.
    /// </summary>
    /// <remarks>
    /// This is the single generation counter for the whole presentation — the host's fade
    /// callbacks and the owner's async content work must both compare against it. Do not keep a
    /// second counter on the owner side: the increment happens inside <see cref="Release"/>
    /// <b>before</b> the owner releases its resources, and that ordering is what stops an already
    /// cancelled async continuation from re-attaching to a torn-down presentation.
    /// </remarks>
    internal int Generation => _generation;

    internal bool HasOwner(object owner) => ReferenceEquals(_owner, owner);

    internal bool OwnsPrimary(CardTooltipController controller) =>
        ReferenceEquals(_activePrimary, controller);

    internal bool OwnsAuxiliary(AuxiliaryTooltipController controller) =>
        ReferenceEquals(_activeAuxiliary, controller);

    internal bool IsContentActive => _contentRoot?.activeInHierarchy == true;

    // ── Preparation ────────────────────────────────────────────────────────────────────────

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
        // Whether a "Tooltip_Main" descendant exists under every tier and under both the Desktop
        // and Mobile tooltip prefabs is unverified. When it is absent the gate stays null and the
        // primary tooltip is simply left unconcealed — silently, by design, so a missing node
        // degrades the paired presentation instead of throwing mid-hover.
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
        // Capture is ordering-sensitive against the consuming feature's AssignTooltipFrame, which
        // writes backgroundImage.sprite: this Prepare path captures BEFORE that call, while the
        // Show path below captures AFTER it. The restored sprite therefore depends on which path
        // ran. Keep both capture points where they are — swapping their relative order produces a
        // wrong-tier auxiliary tooltip frame, which no test or compiler catches.
        _preparedNativeHost = NativeAuxiliaryHostState.Capture(auxiliary);
        // auxParent (Tooltip_Aux_Content in the Tooltip_Aux_P prefab) owns the COMPLETE visual
        // tree — Background, TitleText, BodyText and Divider are its children — so this gate is
        // the one effective concealment. Do not gate the controller root instead: the root is a
        // world-space Transform above the prefab's own nested Canvas (Tooltip_Aux_Canvas), and a
        // CanvasGroup there does not propagate into the canvas at all.
        //
        // When the native controller is reused for a rapid re-request, the previous cancellation
        // deliberately left this gate closed. Reuse it as-is: restore-then-recreate briefly puts
        // the restored group (alpha 1) back on the object during the same frame — Destroy is
        // deferred to frame end — which renders the shell for one frame.
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

        // Every current caller hands this BPP-owned request straight back to the native hide
        // sequence. Keep the auxParent gate CLOSED through that teardown: it is the only
        // concealment the native fade cannot rewrite (the serialized CanvasGroup set to zero here
        // is re-raised whenever the native fade-in is still in flight), and every visual child —
        // Background, TitleText, BodyText, Divider — lives under auxParent. The gate is handed
        // back by the next PrepareAuxiliary reuse, by ReleaseForNativeShow when a foreign show takes
        // the controller, or it dies with the despawned controller.
        ConcealNativeAuxiliary(auxiliary);
        if (ReferenceEquals(_activeAuxiliary, auxiliary))
            Release(restoreNativeContent: false);
        else
            RestorePreparedNativeHost(restoreContentVisibility: false);
    }

    /// <summary>
    /// Hands the pooled host to a confirmed new native show, releasing an active custom
    /// presentation when necessary.
    /// </summary>
    /// <remarks>
    /// Native Show does not reactivate header/body, so this handoff restores native geometry and
    /// normalizes those text nodes instead of blindly replaying an inactive captured state.
    /// </remarks>
    internal bool ReleaseForNativeShow(AuxiliaryTooltipController controller)
    {
        var displacedActivePresentation = OwnsAuxiliary(controller);
        if (displacedActivePresentation)
            Release(restoreNativeContent: false);

        RestorePreparedNativeHostForNativeShow(controller);
        RestorePreparedAuxiliaryGate(expectedController: controller);
        return displacedActivePresentation;
    }

    /// <summary>
    /// Conceals a native auxiliary frame that is visible without native or paired content.
    /// </summary>
    /// <remarks>
    /// The native fade-in owns <see cref="BaseTooltipController.tooltipCanvasGroup"/> and can
    /// raise its alpha after a direct concealment. The gate therefore lives on auxParent, below
    /// that animated group and above the complete visual tree, and remains closed until either the
    /// current paired presentation reveals or the next native show restores it.
    /// </remarks>
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

    // ── Open / content ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Takes over the native pair: hides native auxiliary content, mirrors the primary frame, and
    /// clones the native background. Returns false (never throws) when the native shape is not
    /// usable, having already undone any partial work.
    /// </summary>
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
            // Gate creation only fails on a controller whose Destroy is already pending; this
            // takeover cannot be concealed, so report an unusable native shape and let the caller
            // re-request a live controller. Conceal before Release: Release restores the gates to
            // their visible originals while the native header is still active, which is exactly
            // the header-only orphan this presentation must never expose.
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

    /// <summary>
    /// Registers the owner's content root and commits the layout for it.
    /// </summary>
    /// <param name="contentRoot">Root the host will size and, on release, destroy.</param>
    /// <param name="onContentWidthChanged">
    /// Invoked whenever the host resizes the content, so the owner can retune width-dependent
    /// pieces of its own layout. Deliberately a plain float callback: nothing about the owner's
    /// content model crosses this boundary.
    /// </param>
    internal bool AttachContent(GameObject contentRoot, Action<float>? onContentWidthChanged)
    {
        var auxiliary = _activeAuxiliary;
        if (auxiliary == null || contentRoot == null)
            return false;

        _contentRoot = contentRoot;
        _onContentWidthChanged = onContentWidthChanged;
        ApplyContentWidth(auxiliary, _options.PreferredContentWidth);
        ForceRebuildLayout(auxiliary);
        ApplyNativeHeight(auxiliary);
        return true;
    }

    // ── Placement ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Measures, chooses a side, shrinks the content to fit, positions the pair, and reports what
    /// happened. The host performs no logging: the caller owns its reason codes.
    /// </summary>
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

        Canvas.ForceUpdateCanvases();
        TempoUIUtility.ForceRebuildRecursive(primary.PositioningRectTransform);
        TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(primary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(auxiliary.PositioningRectTransform);
        ApplyNativeHeight(auxiliary);

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
            contentWidth: _currentContentWidth,
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

    /// <summary>
    /// Masks the pair for the duration of a content swap, so intermediate
    /// <see cref="ContentSizeFitter"/> passes never reach the screen.
    /// </summary>
    /// <remarks>
    /// Disposal restores the pre-mask alpha even when the body throws — callers rely on the pair
    /// not being stranded at alpha 0 after a failed swap. The host does not orchestrate what
    /// happens inside the scope: applying, reverting and re-positioning are the caller's business.
    /// </remarks>
    internal IDisposable BeginMaskedLayout() => new MaskedLayoutScope(this);

    /// <summary>Rebuilds the native auxiliary layout and re-applies its height.</summary>
    internal void RebuildLayout()
    {
        var auxiliary = _activeAuxiliary;
        if (auxiliary == null)
            return;

        ForceRebuildLayout(auxiliary);
        ApplyNativeHeight(auxiliary);
    }

    // ── Visibility ─────────────────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Finishes a hide synchronously, skipping any in-flight fade.
    /// </summary>
    /// <remarks>
    /// The fade coroutine is hosted on the native <see cref="AuxiliaryTooltipController"/>, whose
    /// lifetime this host does not control: if that MonoBehaviour is deactivated or destroyed
    /// mid-fade the coroutine dies silently and the completion callback never arrives. Generation
    /// checks reject <i>late</i> callbacks but cannot rescue one that never fires, so the consuming
    /// feature must call this from whatever native signal tells it the tooltip is going away.
    /// </remarks>
    internal void ForceSettle()
    {
        // Deliberately not CompleteAnimatedHide: that also asks the tooltip parent to hide the
        // native auxiliary controller, which is right when *we* decided to hide but wrong here —
        // the native side is already tearing itself down and re-entering it changes behavior.
        // Release() stops any in-flight fade on its own.
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

    // ── Release ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tears the presentation down. Returns false when there was nothing to release.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Release is two-phase, not once-only.</b> With
    /// <paramref name="restoreNativeContent"/> = false the presentation is dropped but the native
    /// host snapshot and auxiliary gate are deliberately kept, so a later call with true still has
    /// something to hand back. Holding the gate also prevents the native fade from exposing an
    /// empty background shell after custom content is destroyed. That second pass is what restores
    /// the native auxiliary tooltip's header, body and divider; making this idempotent leaves the
    /// game's own auxiliary tooltip permanently blank.
    /// </para>
    /// <para>
    /// The ordering is load-bearing: stop the fade and bump the generation <i>first</i>, then let
    /// the owner drop its async work, and only then destroy UI and restore native state. Releasing
    /// owner resources before the generation is invalidated lets an in-flight continuation slip
    /// through its own identity check and re-attach to a presentation that is being destroyed.
    /// </para>
    /// </remarks>
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

    private void RestorePreparedNativeHost(
        bool restoreContentVisibility = true,
        AuxiliaryTooltipController? expectedController = null
    )
    {
        if (_preparedNativeHost == null)
            return;
        if (
            expectedController != null
            && !ReferenceEquals(_preparedNativeHost.Controller, expectedController)
        )
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

    // ── Native layout plumbing ─────────────────────────────────────────────────────────────

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
        // auxParent and the frame are sibling rects in the native Auxiliary Tooltip prefab.
        // Match the frame's center instead of mirroring its serialized offset; mirroring doubles
        // any inset and makes the left/right content padding visibly asymmetric.
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

    /// <summary>
    /// Applies the owner's final trim prefix and commits the resulting panel height once.
    /// </summary>
    /// <remarks>
    /// The complete tree has already settled at its final width. The host converts the panel's
    /// canvas-space overflow into the owner's local height, while the owner uses its settled row
    /// geometry to apply the whole trim prefix without intermediate layout rebuilds.
    /// </remarks>
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
        // The visibility plan was computed from a fully settled tree, so only the final layout
        // needs committing. The general rebuild path remains two-pass for newly activated nested
        // ContentSizeFitters outside this measured height-budget path.
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

    // ── Canvas-space measurement ───────────────────────────────────────────────────────────

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

    // ── Nested helpers ─────────────────────────────────────────────────────────────────────

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
            // Content swaps are atomic. Do not expose the intermediate ContentSizeFitter passes
            // that otherwise make the paired tooltips jump for a frame.
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
            // AddComponent refuses a target whose Destroy is pending and hands back a dead
            // reference, while every other liveness check on the same object still passes until
            // frame end. A dead gate cannot conceal anything, so fail creation instead of letting
            // the first property read throw.
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
            // DestroyImmediate, not Destroy: a deferred destroy leaves the corpse attached until
            // frame end, so a same-frame re-prepare adopts it via GetComponent and loses its gate
            // when the corpse dies — the ungated native fade-in then flashes the tooltip shell.
            // The restore-to-original alpha above is also only safe when the component leaves the
            // hierarchy within the same frame.
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
            // A pooled auxiliary can arrive here after Combat Impact deliberately kept the
            // native text nodes inactive through teardown. The native Show method only assigns
            // strings; it never reactivates header/body, so restoring that captured inactive
            // state would render a layout-collapsed background with no content. A confirmed new
            // native show owns fresh content: restore geometry/art, normalize its text nodes, and
            // leave divider visibility to the native showDivider argument that runs next.
            Restore(restoreContentVisibility: false);
            if (Controller.headerText != null)
                Controller.headerText.gameObject.SetActive(true);
            if (Controller.bodyText != null)
                Controller.bodyText.gameObject.SetActive(true);
        }
    }
}
