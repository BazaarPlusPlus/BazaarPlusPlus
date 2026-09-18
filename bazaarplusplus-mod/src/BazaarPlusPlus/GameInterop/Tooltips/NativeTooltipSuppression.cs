#nullable enable
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.Tooltips;

internal enum NativeTooltipSuppressionOwner
{
    ReplayPresentation,
    ReplayVideoRecording,
    EndOfRunCapture,
}

internal enum NativeTooltipCleanFrameState
{
    Dirty,
    Clean,
    Unavailable,
}

internal enum NativeTooltipCleanFrameReasonCode
{
    None,
    SuppressionInactive,
    RequiredShowGatesUnavailable,
    AuthoritativeControllerSnapshotUnavailable,
    ControllerSnapshotUnavailable,
    TooltipUnlockFailed,
    BoardHighlightCleanupFailed,
    CardHoverCleanupFailed,
    SkillHoverCleanupFailed,
    RecapHoverCleanupFailed,
    TooltipHideFailed,
    CardControllerSurfaceUnavailable,
    CardControllerConcealFailed,
    AuxiliaryControllerSurfaceUnavailable,
    AuxiliaryGateUnavailable,
    AuxiliaryControllerConcealFailed,
    AuditException,
}

internal readonly record struct NativeTooltipCleanFrameAudit(
    NativeTooltipCleanFrameState State,
    int ControllerCount = 0,
    NativeTooltipCleanFrameReasonCode ReasonCode = NativeTooltipCleanFrameReasonCode.None,
    int SkippedInactiveControllerCount = 0
);

internal interface INativeTooltipSuppressionLease : IDisposable
{
    NativeTooltipCleanFrameAudit AuditCleanFrame();
}

/// <summary>Pure owner/refcount algebra behind the native suppression adapter.</summary>
internal sealed class NativeTooltipSuppressionOwnershipCore
{
    private readonly int[] _leaseCounts = new int[3];

    internal bool IsActive => _leaseCounts[0] > 0 || _leaseCounts[1] > 0 || _leaseCounts[2] > 0;

    internal void Acquire(NativeTooltipSuppressionOwner owner)
    {
        _leaseCounts[OwnerIndex(owner)]++;
    }

    internal bool Release(NativeTooltipSuppressionOwner owner)
    {
        var index = OwnerIndex(owner);
        if (_leaseCounts[index] > 0)
            _leaseCounts[index]--;
        return !IsActive;
    }

    internal int LeaseCount(NativeTooltipSuppressionOwner owner) => _leaseCounts[OwnerIndex(owner)];

    private static int OwnerIndex(NativeTooltipSuppressionOwner owner) =>
        owner switch
        {
            NativeTooltipSuppressionOwner.ReplayPresentation => 0,
            NativeTooltipSuppressionOwner.ReplayVideoRecording => 1,
            NativeTooltipSuppressionOwner.EndOfRunCapture => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, null),
        };
}

/// <summary>
/// Shared owner-scoped gate for every native tooltip surface used by recording and screenshots.
/// </summary>
internal static class NativeTooltipSuppression
{
    private static readonly object Gate = new();
    private static readonly NativeTooltipSuppressionOwnershipCore Ownership = new();
    private static readonly NativeTooltipControllerTopologyGeneration ControllerTopology = new();
    private static readonly List<AuxiliaryCanvasGate> AuxiliaryGates = new();
    private static bool? _requiredShowGatesInstalled;
    private static bool _suppressionActive;

    internal static bool IsActive => Volatile.Read(ref _suppressionActive);

    internal static void NotifyControllerLifecycleChanged() =>
        ControllerTopology.ObserveControllerLifecycleChange();

    internal static void CapturePatchCapabilities()
    {
        lock (Gate)
            _requiredShowGatesInstalled = ProbeRequiredShowGatesInstalled();
    }

    internal static void ClearPatchCapabilities()
    {
        lock (Gate)
            _requiredShowGatesInstalled = null;
    }

    internal static INativeTooltipSuppressionLease Begin(NativeTooltipSuppressionOwner owner)
    {
        lock (Gate)
        {
            Ownership.Acquire(owner);
            Volatile.Write(ref _suppressionActive, Ownership.IsActive);
        }

        try
        {
            NativeTooltipCleanFrameAudit? preparationFailure = null;
            if (!NativeTooltipAuthoritativeControllers.TryCapture(out var authoritativeControllers))
            {
                preparationFailure = UnavailableAudit(
                    NativeTooltipCleanFrameReasonCode.AuthoritativeControllerSnapshotUnavailable
                );
            }

            var cleanupReason = ClearCurrentHoverAndTooltipState();
            if (
                preparationFailure == null
                && cleanupReason != NativeTooltipCleanFrameReasonCode.None
            )
            {
                preparationFailure = UnavailableAudit(cleanupReason);
            }

            var controllers = new NativeTooltipControllerSnapshot();
            var initialAudit = ConcealAndAuditNativeTooltips(controllers, authoritativeControllers);
            if (
                preparationFailure == null
                && initialAudit.State == NativeTooltipCleanFrameState.Unavailable
            )
            {
                preparationFailure = initialAudit;
            }
            else if (preparationFailure != null)
            {
                preparationFailure = preparationFailure.Value with
                {
                    ControllerCount = initialAudit.ControllerCount,
                    SkippedInactiveControllerCount = initialAudit.SkippedInactiveControllerCount,
                };
            }

            return new Lease(owner, preparationFailure, controllers, authoritativeControllers);
        }
        catch
        {
            Release(owner);
            throw;
        }
    }

    private static NativeTooltipCleanFrameReasonCode ClearCurrentHoverAndTooltipState()
    {
        var reason = NativeTooltipCleanFrameReasonCode.None;
        TryApply(
            () => Data.TooltipParentComponent?.UnlockAllLockedTooltipControllers(),
            NativeTooltipCleanFrameReasonCode.TooltipUnlockFailed,
            ref reason
        );
        TryApply(
            () => Singleton<BoardManager>.Instance?.ClearCardHighlights(),
            NativeTooltipCleanFrameReasonCode.BoardHighlightCleanupFailed,
            ref reason
        );
        TryApply(
            () =>
            {
                foreach (
                    var controller in Object.FindObjectsOfType<CardController>(
                        includeInactive: false
                    )
                )
                {
                    TryApply(
                        controller.TriggerUnhover,
                        NativeTooltipCleanFrameReasonCode.CardHoverCleanupFailed,
                        ref reason
                    );
                    TryApply(
                        () => controller.ResetPosition(),
                        NativeTooltipCleanFrameReasonCode.CardHoverCleanupFailed,
                        ref reason
                    );
                }
            },
            NativeTooltipCleanFrameReasonCode.CardHoverCleanupFailed,
            ref reason
        );
        TryApply(
            () =>
            {
                foreach (
                    var renderer in Object.FindObjectsOfType<SkillProxyRenderer>(
                        includeInactive: false
                    )
                )
                    TryApply(
                        () => renderer.OnPointerExit(null),
                        NativeTooltipCleanFrameReasonCode.SkillHoverCleanupFailed,
                        ref reason
                    );
            },
            NativeTooltipCleanFrameReasonCode.SkillHoverCleanupFailed,
            ref reason
        );
        TryApply(
            () =>
            {
                foreach (
                    var controller in Object.FindObjectsOfType<RecapItemVisualController>(
                        includeInactive: false
                    )
                )
                    TryApply(
                        () => controller.OnPointerExit(null),
                        NativeTooltipCleanFrameReasonCode.RecapHoverCleanupFailed,
                        ref reason
                    );
            },
            NativeTooltipCleanFrameReasonCode.RecapHoverCleanupFailed,
            ref reason
        );
        TryApply(
            () =>
            {
                var tooltipParent = Data.TooltipParentComponent;
                if (tooltipParent == null)
                    return;
                tooltipParent.HideAuxiliaryTooltipController();
                tooltipParent.HideSecondaryCardTooltipController();
                tooltipParent.HideCardTooltipController();
            },
            NativeTooltipCleanFrameReasonCode.TooltipHideFailed,
            ref reason
        );
        return reason;
    }

    private static NativeTooltipCleanFrameAudit ConcealAndAuditNativeTooltips(
        NativeTooltipControllerSnapshot controllers,
        NativeTooltipAuthoritativeControllers authoritativeControllers
    )
    {
        if (!IsActive)
            return UnavailableAudit(NativeTooltipCleanFrameReasonCode.SuppressionInactive);
        if (!AreRequiredShowGatesInstalled())
        {
            return UnavailableAudit(NativeTooltipCleanFrameReasonCode.RequiredShowGatesUnavailable);
        }

        if (!controllers.TryGet(out var cardControllers, out var auxiliaryControllers))
        {
            return UnavailableAudit(
                NativeTooltipCleanFrameReasonCode.ControllerSnapshotUnavailable
            );
        }

        var controllerCount = cardControllers.Count + auxiliaryControllers.Count;
        var skippedInactiveControllerCount = 0;
        var unavailableReason = NativeTooltipCleanFrameReasonCode.None;
        var dirty = false;

        foreach (var controller in cardControllers)
        {
            if (controller == null)
                continue;
            try
            {
                var isAuthoritative = authoritativeControllers.Owns(controller);
                var isActiveInHierarchy = controller.gameObject.activeInHierarchy;
                if (
                    NativeTooltipControllerAuditCore.ShouldSkipInactiveNonAuthoritative(
                        isAuthoritative,
                        isActiveInHierarchy
                    )
                )
                {
                    skippedInactiveControllerCount++;
                    continue;
                }

                var hider = controller.CanvasHiderComponent;
                if (
                    NativeTooltipControllerAuditCore.Decide(
                        new NativeTooltipControllerAuditCandidate(
                            isAuthoritative,
                            isActiveInHierarchy,
                            HasRequiredSurface: hider != null
                        )
                    ) == NativeTooltipControllerAuditDecision.Unavailable
                )
                {
                    ObserveFailure(
                        ref unavailableReason,
                        NativeTooltipCleanFrameReasonCode.CardControllerSurfaceUnavailable
                    );
                    continue;
                }

                controller.SetLockedFlag(false);
                controller.DisableLockModeCanvasPublic();
                controller.ClearCurrentCard();
                if (hider == null)
                    continue;
                hider.SetVisibility(false);
                if (hider.IsVisible())
                    dirty = true;
            }
            catch
            {
                ObserveFailure(
                    ref unavailableReason,
                    NativeTooltipCleanFrameReasonCode.CardControllerConcealFailed
                );
            }
        }

        try
        {
            PruneDestroyedAuxiliaryGates();
        }
        catch
        {
            ObserveFailure(
                ref unavailableReason,
                NativeTooltipCleanFrameReasonCode.AuxiliaryControllerConcealFailed
            );
        }
        foreach (var controller in auxiliaryControllers)
        {
            if (controller == null)
                continue;
            try
            {
                var isAuthoritative = authoritativeControllers.Owns(controller);
                var isActiveInHierarchy = controller.gameObject.activeInHierarchy;
                if (
                    NativeTooltipControllerAuditCore.ShouldSkipInactiveNonAuthoritative(
                        isAuthoritative,
                        isActiveInHierarchy
                    )
                )
                {
                    skippedInactiveControllerCount++;
                    continue;
                }

                if (
                    NativeTooltipControllerAuditCore.Decide(
                        new NativeTooltipControllerAuditCandidate(
                            isAuthoritative,
                            isActiveInHierarchy,
                            HasRequiredSurface: controller.auxParent != null
                        )
                    ) == NativeTooltipControllerAuditDecision.Unavailable
                )
                {
                    ObserveFailure(
                        ref unavailableReason,
                        NativeTooltipCleanFrameReasonCode.AuxiliaryControllerSurfaceUnavailable
                    );
                    continue;
                }

                var gate = FindAuxiliaryGate(controller);
                if (gate == null)
                {
                    gate = AuxiliaryCanvasGate.TryCreate(controller);
                    if (gate == null)
                    {
                        ObserveFailure(
                            ref unavailableReason,
                            NativeTooltipCleanFrameReasonCode.AuxiliaryGateUnavailable
                        );
                        continue;
                    }
                    AuxiliaryGates.Add(gate);
                }
                if (!gate.ConcealAndAudit())
                    dirty = true;
            }
            catch
            {
                ObserveFailure(
                    ref unavailableReason,
                    NativeTooltipCleanFrameReasonCode.AuxiliaryControllerConcealFailed
                );
            }
        }

        return unavailableReason != NativeTooltipCleanFrameReasonCode.None
            ? UnavailableAudit(unavailableReason, controllerCount, skippedInactiveControllerCount)
            : new NativeTooltipCleanFrameAudit(
                dirty ? NativeTooltipCleanFrameState.Dirty : NativeTooltipCleanFrameState.Clean,
                controllerCount,
                SkippedInactiveControllerCount: skippedInactiveControllerCount
            );
    }

    private static NativeTooltipCleanFrameAudit UnavailableAudit(
        NativeTooltipCleanFrameReasonCode reasonCode,
        int controllerCount = 0,
        int skippedInactiveControllerCount = 0
    ) =>
        new(
            NativeTooltipCleanFrameState.Unavailable,
            controllerCount,
            reasonCode,
            skippedInactiveControllerCount
        );

    private static bool AreRequiredShowGatesInstalled()
    {
        lock (Gate)
        {
            _requiredShowGatesInstalled ??= ProbeRequiredShowGatesInstalled();
            return _requiredShowGatesInstalled.Value;
        }
    }

    private static bool ProbeRequiredShowGatesInstalled()
    {
        try
        {
            return HasOurPrefix(
                    AccessTools.Method(
                        typeof(TooltipParentComponent),
                        nameof(TooltipParentComponent.ShowCardTooltipController)
                    )
                )
                && HasOurPrefix(
                    AccessTools.Method(
                        typeof(TooltipParentComponent),
                        nameof(TooltipParentComponent.ShowSecondaryCardTooltipController)
                    )
                )
                && HasOurPrefix(
                    AccessTools.Method(
                        typeof(TooltipParentComponent),
                        nameof(TooltipParentComponent.ShowAuxiliaryTooltipController)
                    )
                )
                && HasOurPrefix(
                    AccessTools.Method(
                        typeof(CardTooltipController),
                        nameof(CardTooltipController.ShowTooltipController)
                    )
                )
                && HasOurPrefix(
                    AccessTools.Method(
                        typeof(AuxiliaryTooltipController),
                        nameof(AuxiliaryTooltipController.ShowAuxiliaryTooltipController)
                    )
                )
                && HasOurPostfix(AccessTools.DeclaredMethod(typeof(CardTooltipController), "Awake"))
                && HasOurPostfix(
                    AccessTools.DeclaredMethod(typeof(AuxiliaryTooltipController), "Awake")
                )
                && HasOurPrefix(
                    AccessTools.DeclaredMethod(
                        typeof(CardTooltipController),
                        nameof(CardTooltipController.OnDestroy)
                    )
                )
                && HasOurPrefix(
                    AccessTools.DeclaredMethod(
                        typeof(BaseTooltipController),
                        nameof(BaseTooltipController.OnDestroy)
                    )
                );
        }
        catch
        {
            return false;
        }
    }

    private static bool HasOurPrefix(System.Reflection.MethodBase? method)
    {
        if (method == null)
            return false;
        var patchInfo = Harmony.GetPatchInfo(method);
        return patchInfo != null
            && patchInfo.Prefixes.Any(patch => patch.owner == BppPluginMetadata.Guid);
    }

    private static bool HasOurPostfix(System.Reflection.MethodBase? method)
    {
        if (method == null)
            return false;
        var patchInfo = Harmony.GetPatchInfo(method);
        return patchInfo != null
            && patchInfo.Postfixes.Any(patch => patch.owner == BppPluginMetadata.Guid);
    }

    private static AuxiliaryCanvasGate? FindAuxiliaryGate(AuxiliaryTooltipController controller) =>
        AuxiliaryGates.FirstOrDefault(gate => gate.Owns(controller));

    private static void PruneDestroyedAuxiliaryGates()
    {
        for (var index = AuxiliaryGates.Count - 1; index >= 0; index--)
        {
            if (!AuxiliaryGates[index].IsAlive)
                AuxiliaryGates.RemoveAt(index);
        }
    }

    private static void Release(NativeTooltipSuppressionOwner owner)
    {
        var restoreGates = false;
        lock (Gate)
        {
            restoreGates = Ownership.Release(owner);
            Volatile.Write(ref _suppressionActive, Ownership.IsActive);
        }

        if (!restoreGates)
            return;
        try
        {
            for (var index = AuxiliaryGates.Count - 1; index >= 0; index--)
            {
                try
                {
                    AuxiliaryGates[index].Restore();
                }
                catch
                {
                    // Continue restoring independent gates after one native object disappears.
                }
            }
        }
        finally
        {
            AuxiliaryGates.Clear();
        }
    }

    private static void ObserveFailure(
        ref NativeTooltipCleanFrameReasonCode reason,
        NativeTooltipCleanFrameReasonCode failure
    )
    {
        if (reason == NativeTooltipCleanFrameReasonCode.None)
            reason = failure;
    }

    private static void TryApply(
        Action action,
        NativeTooltipCleanFrameReasonCode failure,
        ref NativeTooltipCleanFrameReasonCode reason
    )
    {
        try
        {
            action();
        }
        catch
        {
            ObserveFailure(ref reason, failure);
        }
    }

    private sealed class Lease : INativeTooltipSuppressionLease
    {
        private readonly NativeTooltipSuppressionOwner _owner;
        private readonly NativeTooltipCleanFrameAudit? _preparationFailure;
        private readonly NativeTooltipControllerSnapshot _controllers;
        private readonly NativeTooltipAuthoritativeControllers _authoritativeControllers;
        private bool _disposed;

        internal Lease(
            NativeTooltipSuppressionOwner owner,
            NativeTooltipCleanFrameAudit? preparationFailure,
            NativeTooltipControllerSnapshot controllers,
            NativeTooltipAuthoritativeControllers authoritativeControllers
        )
        {
            _owner = owner;
            _preparationFailure = preparationFailure;
            _controllers = controllers;
            _authoritativeControllers = authoritativeControllers;
        }

        public NativeTooltipCleanFrameAudit AuditCleanFrame()
        {
            if (_disposed || !IsActive)
            {
                return UnavailableAudit(NativeTooltipCleanFrameReasonCode.SuppressionInactive);
            }
            if (_preparationFailure != null)
                return _preparationFailure.Value;

            return ConcealAndAuditNativeTooltips(_controllers, _authoritativeControllers);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                Release(_owner);
            }
            catch
            {
                // Native objects may disappear during state teardown; lease accounting is final.
            }
        }
    }

    private readonly record struct NativeTooltipAuthoritativeControllers(
        CardTooltipController? Primary,
        CardTooltipController? Secondary,
        AuxiliaryTooltipController? Auxiliary
    )
    {
        internal bool Owns(CardTooltipController controller) =>
            ReferenceEquals(Primary, controller) || ReferenceEquals(Secondary, controller);

        internal bool Owns(AuxiliaryTooltipController controller) =>
            ReferenceEquals(Auxiliary, controller);

        internal static bool TryCapture(out NativeTooltipAuthoritativeControllers controllers)
        {
            controllers = default;
            try
            {
                var parent = Data.TooltipParentComponent;
                if (parent == null)
                    return true;

                controllers = new NativeTooltipAuthoritativeControllers(
                    parent.CardTooltipController,
                    parent.SecondaryCardTooltipController,
                    parent.AuxiliaryTooltipController
                );
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class NativeTooltipControllerSnapshot
    {
        private readonly NativeTooltipControllerCacheCore<CardTooltipController> _cardControllers =
            new();
        private readonly NativeTooltipControllerCacheCore<AuxiliaryTooltipController> _auxiliaryControllers =
            new();
        private int _observedControllerTopologyGeneration = int.MinValue;
        private int _observedParentInstanceId = int.MinValue;
        private int _observedParentChildCount = int.MinValue;
        private int _cacheGeneration;

        internal bool TryGet(
            out IReadOnlyList<CardTooltipController> cardControllers,
            out IReadOnlyList<AuxiliaryTooltipController> auxiliaryControllers
        )
        {
            cardControllers = [];
            auxiliaryControllers = [];
            try
            {
                var parent = Data.TooltipParentComponent;
                var parentInstanceId = parent == null ? 0 : parent.GetInstanceID();
                var parentChildCount =
                    parent == null || parent.transform == null ? 0 : parent.transform.childCount;
                var controllerTopologyGeneration = ControllerTopology.Current;
                if (
                    controllerTopologyGeneration != _observedControllerTopologyGeneration
                    || parentInstanceId != _observedParentInstanceId
                    || parentChildCount != _observedParentChildCount
                )
                {
                    _observedControllerTopologyGeneration = controllerTopologyGeneration;
                    _observedParentInstanceId = parentInstanceId;
                    _observedParentChildCount = parentChildCount;
                    _cacheGeneration++;
                }
                cardControllers = _cardControllers.GetOrRefresh(
                    _cacheGeneration,
                    static controller => controller != null,
                    static () =>
                        Object.FindObjectsOfType<CardTooltipController>(includeInactive: true)
                );
                auxiliaryControllers = _auxiliaryControllers.GetOrRefresh(
                    _cacheGeneration,
                    static controller => controller != null,
                    static () =>
                        Object.FindObjectsOfType<AuxiliaryTooltipController>(includeInactive: true)
                );
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class AuxiliaryCanvasGate
    {
        private readonly AuxiliaryTooltipController _controller;
        private readonly CanvasGroup _group;
        private bool _restored;

        private AuxiliaryCanvasGate(AuxiliaryTooltipController controller, CanvasGroup group)
        {
            _controller = controller;
            _group = group;
        }

        internal bool IsAlive => _controller != null && _group != null;

        internal static AuxiliaryCanvasGate? TryCreate(AuxiliaryTooltipController controller)
        {
            if (controller == null || controller.auxParent == null)
                return null;
            var group = controller.auxParent.gameObject.AddComponent<CanvasGroup>();
            return group == null ? null : new AuxiliaryCanvasGate(controller, group);
        }

        internal bool Owns(AuxiliaryTooltipController controller) =>
            ReferenceEquals(_controller, controller);

        internal bool ConcealAndAudit()
        {
            if (_restored || _group == null)
                return false;
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            return _group.alpha <= 0f && !_group.interactable && !_group.blocksRaycasts;
        }

        internal void Restore()
        {
            if (_restored || _group == null)
                return;
            _restored = true;
            Object.DestroyImmediate(_group);
        }
    }
}
