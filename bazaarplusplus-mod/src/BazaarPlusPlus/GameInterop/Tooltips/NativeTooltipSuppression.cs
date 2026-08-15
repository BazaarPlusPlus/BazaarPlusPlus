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

internal readonly record struct NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState State);

internal interface INativeTooltipSuppressionLease : IDisposable
{
    NativeTooltipCleanFrameAudit AuditCleanFrame();
}

/// <summary>Pure owner/refcount algebra behind the native suppression adapter.</summary>
internal sealed class NativeTooltipSuppressionOwnershipCore
{
    private readonly int[] _leaseCounts = new int[3];
    private int _totalLeaseCount;

    internal bool IsActive => _totalLeaseCount > 0;

    internal void Acquire(NativeTooltipSuppressionOwner owner)
    {
        var index = OwnerIndex(owner);
        _leaseCounts[index]++;
        _totalLeaseCount++;
    }

    internal bool Release(NativeTooltipSuppressionOwner owner)
    {
        var index = OwnerIndex(owner);
        if (_leaseCounts[index] == 0)
            return !IsActive;

        _leaseCounts[index]--;
        _totalLeaseCount--;
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
    private static readonly List<AuxiliaryCanvasGate> AuxiliaryGates = new();
    private static int _activeLeaseCount;

    internal static bool IsActive => Volatile.Read(ref _activeLeaseCount) > 0;

    internal static INativeTooltipSuppressionLease Begin(NativeTooltipSuppressionOwner owner)
    {
        lock (Gate)
        {
            Ownership.Acquire(owner);
            Volatile.Write(ref _activeLeaseCount, _activeLeaseCount + 1);
        }

        try
        {
            var preparationAvailable = ClearCurrentHoverAndTooltipState();
            preparationAvailable &=
                ConcealAndAuditNativeTooltips().State != NativeTooltipCleanFrameState.Unavailable;
            return new Lease(owner, preparationAvailable);
        }
        catch
        {
            Release(owner);
            throw;
        }
    }

    private static bool ClearCurrentHoverAndTooltipState()
    {
        var available = true;
        TryApply(
            () => Data.TooltipParentComponent?.UnlockAllLockedTooltipControllers(),
            ref available
        );
        TryApply(() => Singleton<BoardManager>.Instance?.ClearCardHighlights(), ref available);
        TryApply(
            () =>
            {
                foreach (
                    var controller in Object.FindObjectsOfType<CardController>(
                        includeInactive: false
                    )
                )
                {
                    TryApply(controller.TriggerUnhover, ref available);
                    TryApply(() => controller.ResetPosition(), ref available);
                }
            },
            ref available
        );
        TryApply(
            () =>
            {
                foreach (
                    var renderer in Object.FindObjectsOfType<SkillProxyRenderer>(
                        includeInactive: false
                    )
                )
                    TryApply(() => renderer.OnPointerExit(null), ref available);
            },
            ref available
        );
        TryApply(
            () =>
            {
                foreach (
                    var controller in Object.FindObjectsOfType<RecapItemVisualController>(
                        includeInactive: false
                    )
                )
                    TryApply(() => controller.OnPointerExit(null), ref available);
            },
            ref available
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
            ref available
        );
        return available;
    }

    private static NativeTooltipCleanFrameAudit ConcealAndAuditNativeTooltips()
    {
        if (!IsActive || !AreRequiredShowGatesInstalled())
            return UnavailableAudit();

        var unavailable = false;
        var dirty = false;
        CardTooltipController[] cardControllers;
        AuxiliaryTooltipController[] auxiliaryControllers;
        try
        {
            cardControllers = Object.FindObjectsOfType<CardTooltipController>(
                includeInactive: true
            );
            auxiliaryControllers = Object.FindObjectsOfType<AuxiliaryTooltipController>(
                includeInactive: true
            );
        }
        catch
        {
            return UnavailableAudit();
        }

        foreach (var controller in cardControllers)
        {
            if (controller == null)
                continue;
            try
            {
                controller.SetLockedFlag(false);
                controller.DisableLockModeCanvasPublic();
                controller.ClearCurrentCard();
                var hider = controller.CanvasHiderComponent;
                if (hider == null)
                {
                    unavailable = true;
                    continue;
                }
                hider.SetVisibility(false);
                if (hider.IsVisible())
                    dirty = true;
            }
            catch
            {
                unavailable = true;
            }
        }

        PruneDestroyedAuxiliaryGates();
        foreach (var controller in auxiliaryControllers)
        {
            if (controller == null)
                continue;
            try
            {
                if (controller.auxParent == null)
                {
                    unavailable = true;
                    continue;
                }

                var gate = FindAuxiliaryGate(controller);
                if (gate == null)
                {
                    gate = AuxiliaryCanvasGate.TryCreate(controller);
                    if (gate == null)
                    {
                        unavailable = true;
                        continue;
                    }
                    AuxiliaryGates.Add(gate);
                }
                if (!gate.ConcealAndAudit())
                    dirty = true;
            }
            catch
            {
                unavailable = true;
            }
        }

        return unavailable
            ? UnavailableAudit()
            : new NativeTooltipCleanFrameAudit(
                dirty ? NativeTooltipCleanFrameState.Dirty : NativeTooltipCleanFrameState.Clean
            );
    }

    private static NativeTooltipCleanFrameAudit UnavailableAudit() =>
        new(NativeTooltipCleanFrameState.Unavailable);

    private static bool AreRequiredShowGatesInstalled()
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
            var before = Ownership.LeaseCount(owner);
            restoreGates = Ownership.Release(owner);
            if (before > 0)
                Volatile.Write(ref _activeLeaseCount, Math.Max(0, _activeLeaseCount - 1));
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

    private static void TryApply(Action action, ref bool available)
    {
        try
        {
            action();
        }
        catch
        {
            available = false;
        }
    }

    private sealed class Lease : INativeTooltipSuppressionLease
    {
        private readonly NativeTooltipSuppressionOwner _owner;
        private readonly bool _preparationAvailable;
        private bool _disposed;

        internal Lease(NativeTooltipSuppressionOwner owner, bool preparationAvailable)
        {
            _owner = owner;
            _preparationAvailable = preparationAvailable;
        }

        public NativeTooltipCleanFrameAudit AuditCleanFrame()
        {
            if (_disposed || !IsActive || !_preparationAvailable)
            {
                return new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Unavailable);
            }

            return ConcealAndAuditNativeTooltips();
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
