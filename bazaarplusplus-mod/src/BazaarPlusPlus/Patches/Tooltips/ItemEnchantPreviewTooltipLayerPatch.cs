#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.ItemEnchantPreview;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

internal static class TooltipLayerOverride
{
    private sealed class CanvasSortingState
    {
        internal CanvasSortingState(int sortingOrder)
        {
            SortingOrder = sortingOrder;
        }

        internal int SortingOrder { get; }

        internal HashSet<int> Owners { get; } = new HashSet<int>();
    }

    private static readonly Dictionary<Canvas, CanvasSortingState> States =
        new Dictionary<Canvas, CanvasSortingState>();

    internal static void SetElevated(CardTooltipController? controller, bool elevated)
    {
        if (controller == null)
            return;

        // Probe evidence (docs/drafts/2026-07-08-tooltip-overlay-third-recurrence.md): the
        // occluder is the OTHER pooled tooltip clone (the locked tooltip with its monster
        // board), not something inside this clone, so sibling order inside this prefab is
        // irrelevant. Every clone's root canvas shares one sorting order and overrideSorting
        // is not settable on a root canvas, so the one working lever is raising this clone's
        // root canvas order one step above the shared value while the preview is visible.
        var ownerId = controller.GetInstanceID();
        Apply(controller.RootCanvasComponent, ownerId, elevated);
    }

    private static void Apply(Canvas? canvas, int ownerId, bool elevated)
    {
        PruneDestroyedCanvases();
        if (canvas == null)
            return;

        if (elevated)
        {
            if (!States.TryGetValue(canvas, out var state))
            {
                state = new CanvasSortingState(canvas.sortingOrder);
                States.Add(canvas, state);
            }

            state.Owners.Add(ownerId);
            canvas.sortingOrder = ItemEnchantPreviewTooltipLayerPolicy.ElevatedSortingOrder(
                state.SortingOrder
            );
            return;
        }

        if (!States.TryGetValue(canvas, out var existing))
            return;

        existing.Owners.Remove(ownerId);
        if (existing.Owners.Count > 0)
            return;

        canvas.sortingOrder = existing.SortingOrder;
        States.Remove(canvas);
    }

    // A canvas destroyed while an elevation owner is still registered (full teardown
    // skips the ResetValues-driven restore) would otherwise pin a dead key forever.
    private static void PruneDestroyedCanvases()
    {
        List<Canvas>? destroyed = null;
        foreach (var canvas in States.Keys)
        {
            // Unity fake-null: == means "destroyed", but the managed reference (the
            // dictionary key we must remove) is still non-null.
            if (canvas == null)
                (destroyed ??= new List<Canvas>()).Add(canvas!);
        }

        if (destroyed == null)
            return;
        foreach (var canvas in destroyed)
            States.Remove(canvas);
    }
}
