#nullable enable
namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppCollectionDockButtonLayoutPlan(
    bool canApply,
    BppDockButtonBounds bounds,
    BppDockButtonObstacle? blocker
)
{
    internal bool CanApply { get; } = canApply;
    internal BppDockButtonBounds Bounds { get; } = bounds;
    internal BppDockButtonObstacle? Blocker { get; } = blocker;
}

internal static class BppCollectionDockButtonLayoutPlanner
{
    private const int MaximumVerticalSlots = 12;
    private static readonly BppDockButtonObstacle ViewportBlocker = new(
        "viewport",
        default,
        isActive: false
    );

    internal static BppCollectionDockButtonLayoutPlan Resolve(
        BppDockButtonBounds viewportBounds,
        BppDockButtonBounds gearBounds,
        float collectionWidth,
        float collectionHeight,
        float gap,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        if (
            !viewportBounds.IsValid
            || !gearBounds.IsValid
            || collectionWidth <= 0f
            || collectionHeight <= 0f
        )
            return new BppCollectionDockButtonLayoutPlan(false, default, null);

        var safeGap = Math.Max(0f, gap);
        var halfCollectionWidth = collectionWidth * 0.5f;
        var collectionCenterX = Math.Clamp(
            gearBounds.CenterX,
            viewportBounds.MinX + halfCollectionWidth,
            viewportBounds.MaxX - halfCollectionWidth
        );
        var upwardPlan = ResolveDirection(
            viewportBounds,
            BppDockButtonBounds.FromCenter(
                collectionCenterX,
                gearBounds.MaxY + safeGap + (collectionHeight * 0.5f),
                collectionWidth,
                collectionHeight
            ),
            collectionHeight + safeGap,
            blockers
        );
        if (upwardPlan.CanApply)
            return upwardPlan;

        var downwardPlan = ResolveDirection(
            viewportBounds,
            BppDockButtonBounds.FromCenter(
                collectionCenterX,
                gearBounds.MinY - safeGap - (collectionHeight * 0.5f),
                collectionWidth,
                collectionHeight
            ),
            -(collectionHeight + safeGap),
            blockers
        );
        if (downwardPlan.CanApply)
            return downwardPlan;

        return new BppCollectionDockButtonLayoutPlan(
            false,
            upwardPlan.Bounds,
            upwardPlan.Blocker ?? downwardPlan.Blocker
        );
    }

    /// <summary>
    /// A button left of the gear, level with it: the corner's other arm, beside the column
    /// <see cref="Resolve"/> stacks on the gear. Only the slot next to the gear counts: a button
    /// stepped further left reads as unrelated to it, so a blocked slot places nothing and names
    /// the blocker, and the next layout probe tries again.
    /// </summary>
    internal static BppCollectionDockButtonLayoutPlan ResolveBeside(
        BppDockButtonBounds viewportBounds,
        BppDockButtonBounds gearBounds,
        float width,
        float height,
        float gap,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        if (!viewportBounds.IsValid || !gearBounds.IsValid || width <= 0f || height <= 0f)
            return new BppCollectionDockButtonLayoutPlan(false, default, null);

        var safeGap = Math.Max(0f, gap);
        var centerY = Math.Clamp(
            gearBounds.CenterY,
            viewportBounds.MinY + height * 0.5f,
            viewportBounds.MaxY - height * 0.5f
        );
        var candidate = BppDockButtonBounds.FromCenter(
            gearBounds.MinX - safeGap - (width * 0.5f),
            centerY,
            width,
            height
        );
        if (!viewportBounds.Contains(candidate))
            return new BppCollectionDockButtonLayoutPlan(false, candidate, ViewportBlocker);
        var blocker = FindBlocker(candidate, blockers);
        return new BppCollectionDockButtonLayoutPlan(blocker == null, candidate, blocker);
    }

    private static BppCollectionDockButtonLayoutPlan ResolveDirection(
        BppDockButtonBounds viewportBounds,
        BppDockButtonBounds candidate,
        float verticalStep,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        BppDockButtonObstacle? lastBlocker = null;
        for (var slot = 0; slot < MaximumVerticalSlots; slot++)
        {
            if (!viewportBounds.Contains(candidate))
            {
                lastBlocker ??= ViewportBlocker;
                break;
            }

            var blocker = FindBlocker(candidate, blockers);
            if (blocker == null)
                return new BppCollectionDockButtonLayoutPlan(true, candidate, lastBlocker);

            lastBlocker = blocker;
            candidate = BppDockButtonBounds.FromCenter(
                candidate.CenterX,
                candidate.CenterY + verticalStep,
                candidate.Width,
                candidate.Height
            );
        }

        return new BppCollectionDockButtonLayoutPlan(false, candidate, lastBlocker);
    }

    private static BppDockButtonObstacle? FindBlocker(
        BppDockButtonBounds candidate,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        if (blockers == null)
            return null;

        foreach (var blocker in blockers)
        {
            if (blocker.IsActive && candidate.Overlaps(blocker.Bounds))
                return blocker;
        }

        return null;
    }
}
