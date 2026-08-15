#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal sealed class PostCombatImpactController : MonoBehaviour
{
    private const int NativeTooltipWaitFrames = 60;
    private const int NativePreviewWaitFrames = 120;
    private const int PrimaryGeometrySettleMaxFrames = 90;
    private const int PositionedGeometrySettleMaxFrames = 30;
    private const int RequiredStableGeometrySamples = 3;
    private const float GeometryEpsilon = 0.5f;
    private const int MaxTransientShowRetries = 2;

    private readonly WaitForEndOfFrame _waitForEndOfFrame = new();
    private readonly GeometrySample _geometrySampleA = new();
    private readonly GeometrySample _geometrySampleB = new();
    private PostCombatImpactModule? _module;
    private IPostCombatImpactTooltipView? _view;
    private Coroutine? _pendingShow;
    private Coroutine? _settlePresentation;
    private Coroutine? _pendingHoverExit;
    private CardTooltipController? _pendingPrimaryTooltip;
    private Transform? _pendingAuxiliaryAnchor;
    private string? _pendingAuxiliaryHeader;
    private AuxiliaryTooltipController? _pendingAuxiliaryController;
    private AuxiliaryTooltipController? _activeAuxiliaryTooltip;
    private CardTooltipController? _activePrimaryTooltip;
    private bool _auxiliaryRequestOutstanding;
    private HoverRequest? _hoveredRequest;
    private string? _selectedSourceId;
    private int _hoverRevision;
    private int _suppressedHoverRevision = -1;
    private int _transientShowRetryRevision = -1;
    private int _transientShowRetries;
    private bool _nativeAuxiliaryTakeoverActive;
    private bool _visibleEmptyAuxiliaryLogged;
    private CombatImpactPerspective _requestedPerspective = CombatImpactPerspective.Caused;

    internal void Initialize(PostCombatImpactModule module, IPostCombatImpactTooltipView view)
    {
        _module = module;
        _view = view;
        module.AttachRuntime(this);
        Events.RecapEnded.AddListener(OnRecapEnded, this);
    }

    internal void SetHoveredRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) => SetHoveredSource(recapVisual, card, recapVisual.transform, tooltipData, tooltipOffset);

    internal void ClearHoveredRecapCard(
        RecapItemVisualController recapVisual,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    ) => ClearHoveredSource(recapVisual, origin, nativeTooltipLocked);

    internal void SetHoveredSkill(
        SkillProxyRenderer skill,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) => SetHoveredSource(skill, card, skill.transform, tooltipData, tooltipOffset);

    internal void ClearHoveredSkill(
        SkillProxyRenderer skill,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    ) => ClearHoveredSource(skill, origin, nativeTooltipLocked);

    private void Update()
    {
        AuditVisibleNativeAuxiliaryTooltip();

        if (!BppHotkeyService.WasShiftPressedThisFrame())
            return;

        if (_view == null || _hoveredRequest == null || !_view.CanSwitchPerspective)
            return;

        if (
            _selectedSourceId == null
            || _pendingShow != null
            || _settlePresentation != null
            || _activePrimaryTooltip == null
            || _activeAuxiliaryTooltip == null
            || !_view.IsContentActive
        )
            return;

        var activeBefore = _requestedPerspective;
        var perspective =
            activeBefore == CombatImpactPerspective.Caused
                ? CombatImpactPerspective.Received
                : CombatImpactPerspective.Caused;

        try
        {
            if (!_view.SetPerspective(perspective, _hoveredRequest.Anchor))
                return;
            _requestedPerspective = perspective;
            LogInteraction(
                perspective == CombatImpactPerspective.Received
                    ? PostCombatImpactReasonCode.PerspectiveReceived
                    : PostCombatImpactReasonCode.PerspectiveCaused
            );
        }
        catch (Exception ex)
        {
            _requestedPerspective = activeBefore;
            _suppressedHoverRevision = _hoverRevision;
            HideActiveSelection();
            LogInteractionFailure(PostCombatImpactReasonCode.TooltipRenderException, ex);
        }
    }

    private void SetHoveredSource(
        Component owner,
        Card card,
        Transform anchor,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    )
    {
        CancelPendingHoverExit();
        if (card.InstanceId.Value is not { Length: > 0 } sourceId)
        {
            LogInteraction(PostCombatImpactReasonCode.SourceIdUnavailable);
            return;
        }

        RecoverDroppedAuxiliaryRequestIfInactive();
        var sameHover =
            _hoveredRequest != null
            && ReferenceEquals(_hoveredRequest.Owner, owner)
            && string.Equals(_hoveredRequest.SourceId, sourceId, StringComparison.Ordinal);
        if (sameHover && HasWorkForCurrentHover(sourceId))
            return;

        tooltipData ??= CardTooltipData.CreateCardTooltipData(card);
        if (tooltipData == null)
        {
            LogInteraction(PostCombatImpactReasonCode.TooltipDataUnavailable);
            return;
        }

        if (!sameHover)
        {
            _hoverRevision++;
            _suppressedHoverRevision = -1;
        }

        _hoveredRequest = new HoverRequest(
            owner,
            card,
            anchor,
            tooltipData,
            tooltipOffset,
            sourceId
        );
        CancelAndClearPresentation(preserveOutstandingAuxiliaryRequest: true);
        if (!sameHover)
            LogInteractionTrace(
                PostCombatImpactReasonCode.RecapHoverObserved,
                card.TemplateId,
                detail: "hover"
            );
        StartPendingShow();
        if (_pendingShow == null && _settlePresentation == null)
            LogInteractionTrace(
                PostCombatImpactReasonCode.PendingShowBlocked,
                card.TemplateId,
                DescribePendingShowBlock()
            );
        TryPrepareCurrentPrimaryTooltip(card);
    }

    private bool HasWorkForCurrentHover(string sourceId) =>
        _suppressedHoverRevision == _hoverRevision
        || _pendingShow != null
        || _settlePresentation != null
        || _auxiliaryRequestOutstanding
        || _pendingAuxiliaryController != null
        || string.Equals(_selectedSourceId, sourceId, StringComparison.Ordinal);

    private void ClearHoveredSource(
        Component owner,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    )
    {
        if (_hoveredRequest == null || !ReferenceEquals(_hoveredRequest.Owner, owner))
            return;

        var hadWork =
            _selectedSourceId != null
            || _pendingShow != null
            || _settlePresentation != null
            || _auxiliaryRequestOutstanding
            || _pendingAuxiliaryController != null;
        if (
            nativeTooltipLocked
            && origin
                is PostCombatImpactHoverExitOrigin.RecapPointerExit
                    or PostCombatImpactHoverExitOrigin.SkillPointerExit
        )
        {
            _pendingHoverExit ??= StartCoroutine(
                DismissAfterLockedPointerExit(owner, _hoverRevision, hadWork)
            );
            return;
        }
        var dismissedTemplateId = _hoveredRequest.Card.TemplateId;
        ClearHoveredSource();
        CancelAndClearPresentation(preserveOutstandingAuxiliaryRequest: true);
        if (hadWork)
            LogInteractionTrace(
                PostCombatImpactReasonCode.Dismissed,
                dismissedTemplateId,
                detail: "pointer_exit"
            );
    }

    private System.Collections.IEnumerator DismissAfterLockedPointerExit(
        Component owner,
        int revision,
        bool hadWork
    )
    {
        yield return new WaitForSecondsRealtime(0.18f);
        _pendingHoverExit = null;
        if (
            _hoverRevision != revision
            || _hoveredRequest == null
            || !ReferenceEquals(_hoveredRequest.Owner, owner)
        )
            yield break;

        var dismissedTemplateId = _hoveredRequest.Card.TemplateId;
        ClearHoveredSource();
        CancelAndClearPresentation(preserveOutstandingAuxiliaryRequest: true);
        if (hadWork)
            LogInteractionTrace(
                PostCombatImpactReasonCode.Dismissed,
                dismissedTemplateId,
                detail: "locked_pointer_exit"
            );
    }

    private void CancelPendingHoverExit()
    {
        if (_pendingHoverExit == null)
            return;
        StopCoroutine(_pendingHoverExit);
        _pendingHoverExit = null;
    }

    private void StartPendingShow()
    {
        if (
            _pendingShow != null
            || _settlePresentation != null
            || _hoveredRequest == null
            || _suppressedHoverRevision == _hoverRevision
            || _auxiliaryRequestOutstanding
        )
            return;

        _pendingShow = StartCoroutine(ShowWhenReady(_hoveredRequest, _hoverRevision));
    }

    private System.Collections.IEnumerator ShowWhenReady(HoverRequest request, int revision)
    {
        // StartCoroutine advances immediately to the first yield before its return value can be
        // assigned to _pendingShow.
        yield return null;

        if (!IsCurrentHover(request, revision) || !IsRecapOpen())
        {
            LogInteractionTrace(
                PostCombatImpactReasonCode.PendingShowAborted,
                request.Card.TemplateId,
                IsRecapOpen() ? "hover_changed" : "recap_closed"
            );
            _pendingShow = null;
            yield break;
        }

        if (_module == null || _view == null)
        {
            FailPendingShow(
                revision,
                PostCombatImpactReasonCode.RuntimeUnavailable,
                suppressUntilExit: true
            );
            yield break;
        }

        var tooltipParent = TheBazaar.Data.TooltipParentComponent;
        if (tooltipParent == null)
        {
            FailPendingShow(
                revision,
                PostCombatImpactReasonCode.RuntimeUnavailable,
                suppressUntilExit: true
            );
            yield break;
        }

        if (tooltipParent.GetCardTooltipController(request.Card) == null)
        {
            tooltipParent.ShowCardTooltipController(
                request.Anchor,
                request.TooltipOffset,
                request.TooltipData
            );
        }

        CardTooltipController? primary = null;
        for (var frame = 0; frame < NativeTooltipWaitFrames; frame++)
        {
            if (!IsCurrentHover(request, revision) || !IsRecapOpen())
            {
                _pendingShow = null;
                yield break;
            }

            primary = tooltipParent.GetCardTooltipController(request.Card);
            if (primary != null)
            {
                _pendingPrimaryTooltip = primary;
                _view.PrepareNativePrimary(primary);
            }
            if (primary != null && primary.HasShown)
                break;
            yield return null;
        }

        if (primary == null || !primary.HasShown)
        {
            FailPendingShow(
                revision,
                PostCombatImpactReasonCode.PrimaryTooltipCreateTimedOut,
                suppressUntilExit: true
            );
            yield break;
        }

        _pendingPrimaryTooltip = primary;
        // Native positioning clears the lock as it completes. Apply it only after HasShown so the
        // Recap proxy's board CardController cannot tear the primary Tooltip down on the next tick.
        primary.SetLockedFlag(true);
        SuppressTooltipRaycasts(primary);

        if (!IsCurrentHover(request, revision))
        {
            StopPendingShow(hidePrimary: true, preserveOutstandingAuxiliaryRequest: true);
            yield break;
        }

        if (_pendingAuxiliaryController == null)
        {
            var requestSlotAvailable = false;
            for (var frame = 0; frame < NativeTooltipWaitFrames; frame++)
            {
                if (!IsCurrentHover(request, revision) || !IsRecapOpen())
                {
                    StopPendingShow(hidePrimary: true, preserveOutstandingAuxiliaryRequest: true);
                    yield break;
                }

                if (CanIssueNativeAuxiliaryRequest(tooltipParent))
                {
                    requestSlotAvailable = true;
                    break;
                }
                yield return null;
            }

            if (!requestSlotAvailable)
            {
                FailPendingShow(
                    revision,
                    PostCombatImpactReasonCode.AuxiliaryTooltipCreateTimedOut,
                    suppressUntilExit: true
                );
                yield break;
            }

            _pendingAuxiliaryAnchor = request.Anchor;
            _pendingAuxiliaryHeader = _view.Header;
            _auxiliaryRequestOutstanding = true;
            tooltipParent.ShowAuxiliaryTooltipController(
                request.Anchor,
                request.TooltipOffset,
                _pendingAuxiliaryHeader,
                itemTier: request.Card.Tier
            );
            if (_auxiliaryRequestOutstanding && !IsAuxiliaryNodeActive(tooltipParent))
            {
                ClearResolvedAuxiliaryRequest();
                FailPendingShow(
                    revision,
                    PostCombatImpactReasonCode.AuxiliaryTooltipCreateTimedOut,
                    suppressUntilExit: true
                );
                yield break;
            }
        }

        AuxiliaryTooltipController? auxiliary = null;
        for (var frame = 0; frame < NativeTooltipWaitFrames; frame++)
        {
            if (!IsCurrentHover(request, revision) || !IsRecapOpen())
            {
                StopPendingShow(hidePrimary: true, preserveOutstandingAuxiliaryRequest: true);
                yield break;
            }

            auxiliary = _pendingAuxiliaryController;
            if (_auxiliaryRequestOutstanding && !IsAuxiliaryNodeActive(tooltipParent))
            {
                ClearResolvedAuxiliaryRequest();
                break;
            }
            if (
                auxiliary != null
                && tooltipParent.IsAuxiliaryTooltipDisplayed
                && auxiliary._coroutine == null
                && auxiliary.HasShown
            )
                break;
            yield return null;
        }

        if (
            auxiliary == null
            || !tooltipParent.IsAuxiliaryTooltipDisplayed
            || auxiliary._coroutine != null
            || !auxiliary.HasShown
        )
        {
            FailPendingShow(
                revision,
                PostCombatImpactReasonCode.AuxiliaryTooltipCreateTimedOut,
                suppressUntilExit: true
            );
            yield break;
        }

        if (
            !IsCurrentHover(request, revision)
            || !ReferenceEquals(tooltipParent.GetCardTooltipController(request.Card), primary)
        )
        {
            StopPendingShow(hidePrimary: true, preserveOutstandingAuxiliaryRequest: false);
            yield break;
        }

        ClearResolvedAuxiliaryRequest();

        CombatImpactSource? source = null;
        if (_module.TryGetSource(request.SourceId, out var matchedSource))
            source = matchedSource;
        CombatImpactReceived? received = null;
        if (_module.TryGetReceived(request.SourceId, out var matchedReceived))
            received = matchedReceived;
        var perspective = received == null ? CombatImpactPerspective.Caused : _requestedPerspective;
        var entityName =
            source?.Entity.Name
            ?? received?.Entity.Name
            ?? CombatImpactEntitySnapshotReader.ResolveDisplayName(
                request.Card,
                request.TooltipData.GetTitle()
            );
        var isSkill = request.Card.Type == ECardType.Skill;

        bool shown;
        try
        {
            auxiliary.AssignTooltipFrame(request.Card.Tier);
            shown = _view.Show(
                auxiliary,
                primary,
                entityName,
                isSkill,
                source,
                received,
                perspective
            );
        }
        catch (Exception ex)
        {
            FailPendingShow(
                revision,
                PostCombatImpactReasonCode.TooltipRenderException,
                suppressUntilExit: true,
                ex
            );
            yield break;
        }

        if (!shown)
        {
            if (
                IsCurrentHover(request, revision)
                && IsRecapOpen()
                && TryConsumeTransientShowRetry(revision)
            )
            {
                // TryOpen fails soft when the native controller's Destroy is already pending — a
                // state no managed liveness check can see before frame end. Discard the dying
                // auxiliary and re-request a fresh one instead of suppressing the still-valid
                // hover.
                var deadAuxiliary = _pendingAuxiliaryController;
                if (deadAuxiliary != null)
                {
                    _view.CancelPreparedNativeAuxiliary(deadAuxiliary);
                    _pendingAuxiliaryController = null;
                    tooltipParent.HideAuxiliaryTooltipController();
                }
                _pendingShow = null;
                LogInteraction(PostCombatImpactReasonCode.AuxiliaryTooltipShowRetried);
                StartPendingShow();
                yield break;
            }
            FailPendingShow(
                revision,
                PostCombatImpactReasonCode.AuxiliaryTooltipContentUnavailable,
                suppressUntilExit: true
            );
            yield break;
        }

        _pendingAuxiliaryController = null;
        _pendingPrimaryTooltip = null;
        _selectedSourceId = request.SourceId;
        _activePrimaryTooltip = primary;
        _activeAuxiliaryTooltip = auxiliary;
        _pendingShow = null;
        _settlePresentation = StartCoroutine(
            SettleAndPositionPresentation(
                request,
                revision,
                auxiliary,
                primary,
                source != null || received != null
            )
        );
    }

    private System.Collections.IEnumerator SettleAndPositionPresentation(
        HoverRequest request,
        int revision,
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        bool hasAttributedImpact
    )
    {
        // StartCoroutine advances immediately; yield before the controller stores the handle.
        yield return _waitForEndOfFrame;

        var previous = _geometrySampleA;
        var current = _geometrySampleB;
        var hasPrevious = false;
        var stableSamples = 0;
        var primaryConverged = false;
        for (var frame = 0; frame < PrimaryGeometrySettleMaxFrames; frame++)
        {
            if (!IsCurrentPresentation(request, revision, auxiliary, primary))
            {
                _settlePresentation = null;
                HideActiveSelection();
                yield break;
            }

            if (TryCaptureGeometry(primary, auxiliary: null, current))
            {
                stableSamples =
                    hasPrevious && current.IsNear(previous, GeometryEpsilon)
                        ? stableSamples + 1
                        : 1;
                (previous, current) = (current, previous);
                hasPrevious = true;
                if (stableSamples >= RequiredStableGeometrySamples)
                {
                    primaryConverged = true;
                    break;
                }
            }
            else
            {
                hasPrevious = false;
                stableSamples = 0;
            }

            yield return _waitForEndOfFrame;
        }

        if (!primaryConverged)
        {
            LogInteractionDegraded(PostCombatImpactReasonCode.PrimaryGeometrySettleTimedOut);
        }

        var previewsReady = false;
        for (var frame = 0; frame < NativePreviewWaitFrames; frame++)
        {
            if (!IsCurrentPresentation(request, revision, auxiliary, primary))
            {
                _settlePresentation = null;
                HideActiveSelection();
                yield break;
            }
            if (_view?.IsReadyToReveal == true)
            {
                previewsReady = true;
                break;
            }
            yield return _waitForEndOfFrame;
        }
        if (!previewsReady)
            LogInteractionDegraded(PostCombatImpactReasonCode.EntityPreviewCreateTimedOut);
        else
            yield return _waitForEndOfFrame;

        bool positioned;
        try
        {
            positioned = _view?.Position(auxiliary, primary, request.Anchor) == true;
        }
        catch (Exception ex)
        {
            _settlePresentation = null;
            _suppressedHoverRevision = revision;
            HideActiveSelection();
            LogInteractionFailure(PostCombatImpactReasonCode.TooltipRenderException, ex);
            yield break;
        }

        if (!positioned)
        {
            _settlePresentation = null;
            HideActiveSelection();
            _suppressedHoverRevision = revision;
            LogInteraction(PostCombatImpactReasonCode.AuxiliaryTooltipPositionUnavailable);
            yield break;
        }

        hasPrevious = false;
        stableSamples = 0;
        var pairConverged = false;
        for (var frame = 0; frame < PositionedGeometrySettleMaxFrames; frame++)
        {
            yield return _waitForEndOfFrame;
            if (!IsCurrentPresentation(request, revision, auxiliary, primary))
            {
                _settlePresentation = null;
                HideActiveSelection();
                yield break;
            }

            if (TryCaptureGeometry(primary, auxiliary, current))
            {
                stableSamples =
                    hasPrevious && current.IsNear(previous, GeometryEpsilon)
                        ? stableSamples + 1
                        : 1;
                (previous, current) = (current, previous);
                hasPrevious = true;
                if (stableSamples >= RequiredStableGeometrySamples)
                {
                    pairConverged = true;
                    break;
                }
            }
            else
            {
                hasPrevious = false;
                stableSamples = 0;
            }
        }

        if (!pairConverged)
            LogInteractionDegraded(PostCombatImpactReasonCode.PairGeometrySettleTimedOut);

        try
        {
            _view?.Reveal();
        }
        catch (Exception ex)
        {
            LogInteractionFailure(PostCombatImpactReasonCode.TooltipRenderException, ex);
        }

        _settlePresentation = null;
        LogInteractionTrace(
            !hasAttributedImpact
                ? PostCombatImpactReasonCode.ShownWithoutAttributedImpact
                : PostCombatImpactReasonCode.Shown,
            request.Card.TemplateId,
            detail: "settled"
        );
    }

    private bool IsCurrentPresentation(
        HoverRequest request,
        int revision,
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary
    )
    {
        if (
            !IsCurrentHover(request, revision)
            || !IsRecapOpen()
            || auxiliary == null
            || primary == null
            || !primary.HasShown
        )
            return false;

        return ReferenceEquals(
            TheBazaar.Data.TooltipParentComponent?.GetCardTooltipController(request.Card),
            primary
        );
    }

    private static bool TryCaptureGeometry(
        CardTooltipController primary,
        AuxiliaryTooltipController? auxiliary,
        GeometrySample sample
    )
    {
        var primaryRect = primary.PositioningRectTransform;
        var contentRect = primary.CanvasContentRectTransform;
        if (primaryRect == null || contentRect == null)
            return false;

        sample.Reset(auxiliary == null ? 9 : 13);
        sample.CopyWorldCorners(primaryRect, 0);
        sample.CopyWorldCorners(contentRect, 4);
        sample.SetValue(8, primary.transform.localScale);
        if (auxiliary != null)
            sample.CopyWorldCorners(auxiliary.PositioningRectTransform, 9);
        return true;
    }

    internal void OnNativeTooltipChanging(CardTooltipController controller)
    {
        if (ReferenceEquals(_pendingPrimaryTooltip, controller))
        {
            StopPendingShow(hidePrimary: false, preserveOutstandingAuxiliaryRequest: true);
            return;
        }

        if (_view?.OnNativeTooltipChanging(controller) != true)
            return;

        if (_pendingShow != null || _settlePresentation != null)
            StopPendingShow(hidePrimary: false, preserveOutstandingAuxiliaryRequest: true);
        _selectedSourceId = null;
        _activePrimaryTooltip = null;
        _activeAuxiliaryTooltip = null;
    }

    internal void OnNativeTooltipInteractabilityChanged(
        BaseTooltipController controller,
        CanvasGroup canvasGroup
    )
    {
        if (canvasGroup.blocksRaycasts && OwnsNativeTooltip(controller))
            SuppressTooltipRaycasts(controller);
    }

    private bool OwnsNativeTooltip(BaseTooltipController controller) =>
        ReferenceEquals(_pendingPrimaryTooltip, controller)
        || ReferenceEquals(_activePrimaryTooltip, controller)
        || ReferenceEquals(_pendingAuxiliaryController, controller)
        || ReferenceEquals(_activeAuxiliaryTooltip, controller);

    private static void SuppressTooltipRaycasts(BaseTooltipController controller)
    {
        var canvasGroup = controller.tooltipCanvasGroup;
        if (canvasGroup == null)
            return;

        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    internal void OnNativeTooltipPreparing(
        CardTooltipController controller,
        ITooltipData tooltipData
    )
    {
        if (
            _hoveredRequest == null
            || _view == null
            || (_pendingShow == null && _settlePresentation == null && _selectedSourceId == null)
            || tooltipData is not CardTooltipData cardTooltipData
            || cardTooltipData.CardInstance?.InstanceId.Value is not { Length: > 0 } sourceId
            || !string.Equals(sourceId, _hoveredRequest.SourceId, StringComparison.Ordinal)
        )
            return;

        _pendingPrimaryTooltip = controller;
        _view.PrepareNativePrimary(controller);
    }

    internal void OnNativeAuxiliaryTooltipShowing(
        AuxiliaryTooltipController controller,
        Transform anchor,
        string header,
        string body
    )
    {
        var beforeHandoff = CaptureNativeAuxiliaryState(controller);
        var headerRequested = !string.IsNullOrWhiteSpace(header);
        var bodyRequested = !string.IsNullOrWhiteSpace(body);
        var displacedActiveImpact = _view?.OnNativeAuxiliaryTooltipShowing(controller) == true;
        if (
            NativeAuxiliaryTooltipAnomalyRules.RequestedTextNeedsRecovery(
                headerRequested,
                bodyRequested,
                beforeHandoff.HeaderActive,
                beforeHandoff.BodyActive
            )
        )
        {
            var afterHandoff = CaptureNativeAuxiliaryState(controller);
            var recovered =
                (!headerRequested || afterHandoff.HeaderActive)
                && (!bodyRequested || afterHandoff.BodyActive);
            LogNativeAuxiliaryAnomaly(
                NativeAuxiliaryTooltipAnomalyCategory.RequestedTextInactive,
                NativeAuxiliaryTooltipAnomalyPhase.ShowHandoff,
                beforeHandoff,
                recovered
            );
        }
        if (
            _auxiliaryRequestOutstanding
            && ReferenceEquals(_pendingAuxiliaryAnchor, anchor)
            && string.Equals(_pendingAuxiliaryHeader, header, StringComparison.Ordinal)
        )
        {
            _nativeAuxiliaryTakeoverActive = false;
            _auxiliaryRequestOutstanding = false;
            _pendingAuxiliaryController = controller;
            _view?.PrepareNativeAuxiliary(controller);
            if (_pendingShow == null)
                _pendingShow = StartCoroutine(ResumeAfterNativeAuxiliaryShow(controller));
            return;
        }

        // A show carrying this feature's own header that fails the request match is the orphan
        // hazard: the prepared concealment was just handed back and nothing re-conceals it.
        if (_view != null && string.Equals(header, _view.Header, StringComparison.Ordinal))
            LogInteractionTrace(
                PostCombatImpactReasonCode.NativeAuxiliaryUnmatched,
                _hoveredRequest?.Card.TemplateId ?? Guid.Empty,
                !_auxiliaryRequestOutstanding ? "no_outstanding_request"
                    : !ReferenceEquals(_pendingAuxiliaryAnchor, anchor) ? "anchor_mismatch"
                    : "header_mismatch"
            );

        if (_pendingShow != null || _settlePresentation != null)
        {
            StopPendingShow(hidePrimary: true, preserveOutstandingAuxiliaryRequest: true);
        }

        if (displacedActiveImpact)
        {
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
            _selectedSourceId = null;
            _activePrimaryTooltip = null;
            _activeAuxiliaryTooltip = null;
            LogInteraction(PostCombatImpactReasonCode.NativeAuxiliaryDisplaced);
        }

        _nativeAuxiliaryTakeoverActive = true;
    }

    private void AuditVisibleNativeAuxiliaryTooltip()
    {
        var tooltipParent = TheBazaar.Data.TooltipParentComponent;
        var controller = tooltipParent?.AuxiliaryTooltipController;
        if (
            tooltipParent?.IsAuxiliaryTooltipDisplayed != true
            || controller == null
            || !controller.isActiveAndEnabled
        )
        {
            _visibleEmptyAuxiliaryLogged = false;
            return;
        }

        var state = CaptureNativeAuxiliaryState(controller);
        var pairedContentActive = _view?.IsContentActive == true;
        var visibleWithoutText = NativeAuxiliaryTooltipAnomalyRules.IsVisibleWithoutText(
            state.FrameVisible,
            pairedContentActive,
            state.HeaderRenderable,
            state.BodyRenderable
        );
        if (!visibleWithoutText)
        {
            _visibleEmptyAuxiliaryLogged = false;
            return;
        }
        if (_visibleEmptyAuxiliaryLogged)
            return;

        _visibleEmptyAuxiliaryLogged = true;
        var concealmentApplied = _view?.TryConcealVisibleEmptyNativeAuxiliary(controller) == true;
        var recovered = concealmentApplied && !CaptureNativeAuxiliaryState(controller).FrameVisible;
        LogNativeAuxiliaryAnomaly(
            NativeAuxiliaryTooltipAnomalyCategory.VisibleWithoutText,
            NativeAuxiliaryTooltipAnomalyPhase.FrameAudit,
            state,
            recovered
        );
    }

    private static NativeAuxiliaryTooltipState CaptureNativeAuxiliaryState(
        AuxiliaryTooltipController controller
    )
    {
        var header = controller.headerText;
        var body = controller.bodyText;
        var background = controller.backgroundImage;
        var nativeCanvas = controller.tooltipCanvasGroup;
        var contentGate = controller.auxParent?.GetComponent<CanvasGroup>();
        var headerEmpty = header == null || string.IsNullOrWhiteSpace(header.text);
        var bodyEmpty = body == null || string.IsNullOrWhiteSpace(body.text);
        var headerActive = header != null && header.gameObject.activeSelf;
        var bodyActive = body != null && body.gameObject.activeSelf;
        var frameVisible =
            controller.gameObject.activeInHierarchy
            && nativeCanvas != null
            && nativeCanvas.alpha > NativePairedTooltipMetrics.Epsilon
            && (contentGate == null || contentGate.alpha > NativePairedTooltipMetrics.Epsilon)
            && background != null
            && background.enabled
            && background.gameObject.activeInHierarchy
            && background.color.a > NativePairedTooltipMetrics.Epsilon;
        return new NativeAuxiliaryTooltipState(
            HeaderActive: headerActive,
            BodyActive: bodyActive,
            HeaderEmpty: headerEmpty,
            BodyEmpty: bodyEmpty,
            HeaderRenderable: headerActive
                && header!.enabled
                && header.gameObject.activeInHierarchy
                && header.color.a > NativePairedTooltipMetrics.Epsilon
                && !headerEmpty,
            BodyRenderable: bodyActive
                && body!.enabled
                && body.gameObject.activeInHierarchy
                && body.color.a > NativePairedTooltipMetrics.Epsilon
                && !bodyEmpty,
            FrameVisible: frameVisible
        );
    }

    private void LogNativeAuxiliaryAnomaly(
        NativeAuxiliaryTooltipAnomalyCategory category,
        NativeAuxiliaryTooltipAnomalyPhase phase,
        NativeAuxiliaryTooltipState state,
        bool recovered
    ) =>
        BppLog.WarnEvent(
            PostCombatImpactLogEvents.NativeAuxiliaryAnomaly,
            PostCombatImpactLogEvents.AnomalyCategory.Bind(category),
            PostCombatImpactLogEvents.AnomalyPhase.Bind(phase),
            PostCombatImpactLogEvents.HeaderActive.Bind(state.HeaderActive),
            PostCombatImpactLogEvents.BodyActive.Bind(state.BodyActive),
            PostCombatImpactLogEvents.HeaderEmpty.Bind(state.HeaderEmpty),
            PostCombatImpactLogEvents.BodyEmpty.Bind(state.BodyEmpty),
            PostCombatImpactLogEvents.PairedContentActive.Bind(_view?.IsContentActive == true),
            PostCombatImpactLogEvents.Recovered.Bind(recovered)
        );

    internal void OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller)
    {
        var shouldResumeStableHover =
            _hoveredRequest != null
            && _pendingHoverExit == null
            && _suppressedHoverRevision != _hoverRevision
            && Application.isFocused
            && IsRecapOpen();
        if (_view?.OnNativeAuxiliaryTooltipHiding(controller) == true)
        {
            if (_pendingShow != null || _settlePresentation != null)
            {
                StopPendingShow(hidePrimary: false, preserveOutstandingAuxiliaryRequest: true);
            }
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
            _selectedSourceId = null;
            _activePrimaryTooltip = null;
            _activeAuxiliaryTooltip = null;
            LogInteraction(PostCombatImpactReasonCode.NativeAuxiliaryHidden);
            if (shouldResumeStableHover)
            {
                StartPendingShow();
                LogInteraction(PostCombatImpactReasonCode.NativeAuxiliaryRequeued);
            }
        }

        if (!_nativeAuxiliaryTakeoverActive)
            return;

        _nativeAuxiliaryTakeoverActive = false;
        if (_hoveredRequest != null && _suppressedHoverRevision != _hoverRevision)
            StartPendingShow();
    }

    private System.Collections.IEnumerator ResumeAfterNativeAuxiliaryShow(
        AuxiliaryTooltipController controller
    )
    {
        // The Harmony prefix runs before the native body starts its positioning coroutine.
        yield return null;

        if (!ReferenceEquals(_pendingAuxiliaryController, controller))
        {
            _pendingShow = null;
            yield break;
        }

        if (_hoveredRequest != null && _suppressedHoverRevision != _hoverRevision && IsRecapOpen())
        {
            _pendingShow = null;
            StartPendingShow();
            yield break;
        }

        for (var frame = 0; frame < NativeTooltipWaitFrames; frame++)
        {
            if (controller._coroutine == null)
                break;
            yield return null;
        }

        _view?.CancelPreparedNativeAuxiliary(controller);
        _pendingAuxiliaryController = null;
        ClearResolvedAuxiliaryRequest();
        _pendingShow = null;
        TheBazaar.Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
        LogInteraction(PostCombatImpactReasonCode.StaleRequestDiscarded);
    }

    internal void HideDetails()
    {
        ClearHoveredSource();
        CancelAndClearPresentation(preserveOutstandingAuxiliaryRequest: true);
    }

    private void ClearHoveredSource()
    {
        CancelPendingHoverExit();
        _hoveredRequest = null;
        _hoverRevision++;
        _suppressedHoverRevision = -1;
    }

    private bool IsCurrentHover(HoverRequest request, int revision) =>
        revision == _hoverRevision && ReferenceEquals(_hoveredRequest, request);

    private string DescribePendingShowBlock() =>
        _hoveredRequest == null ? "no_hover"
        : _suppressedHoverRevision == _hoverRevision ? "suppressed"
        : _auxiliaryRequestOutstanding ? "outstanding_request"
        : "unknown";

    /// <summary>
    /// Grants a bounded number of same-hover re-requests after a transient show failure, so a
    /// persistent failure still ends in suppression instead of a hide/show loop.
    /// </summary>
    private bool TryConsumeTransientShowRetry(int revision)
    {
        if (_transientShowRetryRevision != revision)
        {
            _transientShowRetryRevision = revision;
            _transientShowRetries = 0;
        }
        if (_transientShowRetries >= MaxTransientShowRetries)
            return false;
        _transientShowRetries++;
        return true;
    }

    private void TryPrepareCurrentPrimaryTooltip(Card card)
    {
        if (
            _view == null
            || (_pendingShow == null && _settlePresentation == null && _selectedSourceId == null)
        )
            return;
        var primary = TheBazaar.Data.TooltipParentComponent?.GetCardTooltipController(card);
        if (primary == null)
            return;

        _pendingPrimaryTooltip = primary;
        _view.PrepareNativePrimary(primary);
    }

    private void CancelAndClearPresentation(bool preserveOutstandingAuxiliaryRequest)
    {
        StopPendingShow(
            hidePrimary: true,
            preserveOutstandingAuxiliaryRequest: preserveOutstandingAuxiliaryRequest
        );
        HideActiveSelection();
    }

    private void StopPendingShow(bool hidePrimary, bool preserveOutstandingAuxiliaryRequest)
    {
        var pendingPrimary = _pendingPrimaryTooltip;
        var pendingAuxiliary = _pendingAuxiliaryController;
        if (_settlePresentation != null)
        {
            StopCoroutine(_settlePresentation);
            _settlePresentation = null;
        }
        if (_pendingShow != null)
        {
            StopCoroutine(_pendingShow);
            _pendingShow = null;
        }

        if (pendingPrimary != null)
        {
            _view?.CancelPreparedNativePrimary(pendingPrimary);
            pendingPrimary.SetLockedFlag(false);
        }
        _pendingPrimaryTooltip = null;

        if (pendingAuxiliary != null)
        {
            _view?.CancelPreparedNativeAuxiliary(pendingAuxiliary);
            _pendingAuxiliaryController = null;
            TheBazaar.Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
        }

        if (!preserveOutstandingAuxiliaryRequest || !_auxiliaryRequestOutstanding)
            ClearResolvedAuxiliaryRequest();

        if (hidePrimary && pendingPrimary != null)
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
    }

    private void FinishPendingShow(bool hidePrimary)
    {
        var pendingPrimary = _pendingPrimaryTooltip;
        _pendingShow = null;
        if (pendingPrimary != null)
        {
            _view?.CancelPreparedNativePrimary(pendingPrimary);
            pendingPrimary.SetLockedFlag(false);
        }
        _pendingPrimaryTooltip = null;
        if (hidePrimary && pendingPrimary != null)
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
    }

    private void FailPendingShow(
        int revision,
        PostCombatImpactReasonCode reasonCode,
        bool suppressUntilExit,
        Exception? exception = null
    )
    {
        var pendingAuxiliary = _pendingAuxiliaryController;
        if (pendingAuxiliary != null)
        {
            _view?.CancelPreparedNativeAuxiliary(pendingAuxiliary);
            _pendingAuxiliaryController = null;
            TheBazaar.Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
        }

        if (!_auxiliaryRequestOutstanding)
            ClearResolvedAuxiliaryRequest();
        FinishPendingShow(hidePrimary: true);
        if (suppressUntilExit && revision == _hoverRevision)
            _suppressedHoverRevision = revision;
        if (exception == null)
            LogInteraction(reasonCode);
        else
            LogInteractionFailure(reasonCode, exception);
    }

    private void ClearResolvedAuxiliaryRequest()
    {
        _pendingAuxiliaryAnchor = null;
        _pendingAuxiliaryHeader = null;
        _auxiliaryRequestOutstanding = false;
    }

    private void RecoverDroppedAuxiliaryRequestIfInactive()
    {
        var tooltipParent = TheBazaar.Data.TooltipParentComponent;
        if (
            !_auxiliaryRequestOutstanding
            || tooltipParent == null
            || IsAuxiliaryNodeActive(tooltipParent)
        )
            return;

        ClearResolvedAuxiliaryRequest();
    }

    private static bool CanIssueNativeAuxiliaryRequest(TooltipParentComponent tooltipParent)
    {
        var dataModel = tooltipParent._auxiliaryTooltipDataModel;
        if (dataModel == null)
            return false;
        return !TheBazaar.Data.SequenceProcessor.IsNodeActive(dataModel)
            || tooltipParent.AuxiliaryTooltipController != null;
    }

    private static bool IsAuxiliaryNodeActive(TooltipParentComponent tooltipParent)
    {
        var dataModel = tooltipParent._auxiliaryTooltipDataModel;
        return dataModel != null && TheBazaar.Data.SequenceProcessor.IsNodeActive(dataModel);
    }

    private void HideActiveSelection()
    {
        var hadSelection = _selectedSourceId != null;
        _view?.Hide();
        if (hadSelection)
            TheBazaar.Data.TooltipParentComponent?.HideCardTooltipController();
        _selectedSourceId = null;
        _activePrimaryTooltip = null;
        _activeAuxiliaryTooltip = null;
    }

    private static bool IsRecapOpen()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        return boardManager != null && boardManager.IsRecapViewOpen;
    }

    private void OnRecapEnded()
    {
        _requestedPerspective = CombatImpactPerspective.Caused;
        HideDetails();
    }

    private static void LogInteraction(PostCombatImpactReasonCode reasonCode) =>
        BppLog.DebugEvent(
            PostCombatImpactLogEvents.InteractionObserved,
            () => [PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)]
        );

    private static void LogInteractionTrace(
        PostCombatImpactReasonCode reasonCode,
        Guid cardTemplateId,
        string detail
    ) =>
        BppLog.DebugEvent(
            PostCombatImpactLogEvents.InteractionTraced,
            () =>
                [
                    PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode),
                    PostCombatImpactLogEvents.Card.Bind(cardTemplateId),
                    PostCombatImpactLogEvents.Detail.Bind(detail),
                ]
        );

    private static void LogInteractionFailure(
        PostCombatImpactReasonCode reasonCode,
        Exception exception
    ) =>
        BppLog.WarnEvent(
            PostCombatImpactLogEvents.InteractionDegraded,
            exception,
            PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)
        );

    private static void LogInteractionDegraded(PostCombatImpactReasonCode reasonCode) =>
        BppLog.WarnEvent(
            PostCombatImpactLogEvents.InteractionDegraded,
            PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)
        );

    private void OnDestroy()
    {
        Events.RecapEnded.RemoveListener(OnRecapEnded);
        HideDetails();
        _view?.Dispose();
        _view = null;
        _module?.DetachRuntime(this);
        _module = null;
    }

    private sealed record HoverRequest(
        Component Owner,
        Card Card,
        Transform Anchor,
        CardTooltipData TooltipData,
        Vector3 TooltipOffset,
        string SourceId
    );

    private readonly record struct NativeAuxiliaryTooltipState(
        bool HeaderActive,
        bool BodyActive,
        bool HeaderEmpty,
        bool BodyEmpty,
        bool HeaderRenderable,
        bool BodyRenderable,
        bool FrameVisible
    );

    private sealed class GeometrySample
    {
        private readonly Vector3[] _values = new Vector3[13];
        private readonly Vector3[] _corners = new Vector3[4];
        private int _count;

        internal void Reset(int count) => _count = count;

        internal void CopyWorldCorners(RectTransform rect, int destinationIndex)
        {
            rect.GetWorldCorners(_corners);
            Array.Copy(_corners, 0, _values, destinationIndex, _corners.Length);
        }

        internal void SetValue(int index, Vector3 value) => _values[index] = value;

        internal bool IsNear(GeometrySample other, float positionEpsilon)
        {
            if (_count != other._count)
                return false;

            var positionEpsilonSquared = positionEpsilon * positionEpsilon;
            for (var index = 0; index < _count; index++)
            {
                if (index == 8)
                {
                    if (
                        Mathf.Abs(_values[index].x - other._values[index].x) > 0.001f
                        || Mathf.Abs(_values[index].y - other._values[index].y) > 0.001f
                        || Mathf.Abs(_values[index].z - other._values[index].z) > 0.001f
                    )
                        return false;
                    continue;
                }

                if ((_values[index] - other._values[index]).sqrMagnitude > positionEpsilonSquared)
                    return false;
            }
            return true;
        }
    }
}
