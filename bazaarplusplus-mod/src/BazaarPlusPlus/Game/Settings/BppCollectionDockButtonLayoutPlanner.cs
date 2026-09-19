#nullable enable
namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppCollectionDockButtonLayoutPlan(
    bool canApply,
    BppDockButtonBounds bounds,
    string? blockerName
)
{
    internal bool CanApply { get; } = canApply;
    internal BppDockButtonBounds Bounds { get; } = bounds;
    internal string? BlockerName { get; } = blockerName;
}

internal static class BppCollectionDockButtonLayoutPlanner
{
    private const int MaximumVerticalSlots = 12;

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
            upwardPlan.BlockerName ?? downwardPlan.BlockerName
        );
    }

    private static BppCollectionDockButtonLayoutPlan ResolveDirection(
        BppDockButtonBounds viewportBounds,
        BppDockButtonBounds candidate,
        float verticalStep,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        string? lastBlockerName = null;
        for (var slot = 0; slot < MaximumVerticalSlots; slot++)
        {
            if (!viewportBounds.Contains(candidate))
            {
                lastBlockerName ??= "viewport";
                break;
            }

            var blocker = FindBlocker(candidate, blockers);
            if (blocker == null)
                return new BppCollectionDockButtonLayoutPlan(true, candidate, lastBlockerName);

            lastBlockerName = blocker.Value.Name;
            candidate = BppDockButtonBounds.FromCenter(
                candidate.CenterX,
                candidate.CenterY + verticalStep,
                candidate.Width,
                candidate.Height
            );
        }

        return new BppCollectionDockButtonLayoutPlan(false, candidate, lastBlockerName);
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
