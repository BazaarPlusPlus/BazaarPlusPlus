#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.GameInterop.Tooltips;

internal static class EndOfRunCaptureSamplingPerformanceTests
{
    internal static void Run()
    {
        Heavy_sampling_has_a_wall_clock_frequency_cap();
        Sampling_plan_reuses_and_invalidates_by_generation();
        Sampling_plan_prunes_replaced_and_destroyed_entries();
        Excluded_hierarchy_sentinels_reject_equal_count_replacements();
        Tooltip_snapshot_reuses_until_generation_or_lifetime_changes();
        Tooltip_lifecycle_signals_invalidate_equal_count_and_deep_topology_changes();
        Tooltip_audit_skips_only_inactive_non_authoritative_controllers();
    }

    private static void Heavy_sampling_has_a_wall_clock_frequency_cap()
    {
        foreach (var framesPerSecond in new[] { 60, 120, 240 })
        {
            var cadence = new EndOfRunHeavySampleCadence();
            var pollCount = framesPerSecond * 5 + 1;
            var sampleCount = 0;
            for (var frame = 0; frame < pollCount; frame++)
            {
                var now = frame / (float)framesPerSecond;
                if (cadence.ShouldSample(now, generation: 1))
                    sampleCount++;
            }

            var maximum = (int)Math.Ceiling(5f / EndOfRunHeavySampleCadence.IntervalSeconds) + 1;
            Assert(
                sampleCount <= maximum,
                $"{framesPerSecond} FPS produced {sampleCount} heavy samples; expected <= {maximum}."
            );
            Assert(
                pollCount > sampleCount * 4,
                "The workflow poll must remain materially more frequent than heavy sampling."
            );
        }

        var generationCadence = new EndOfRunHeavySampleCadence();
        Assert(generationCadence.ShouldSample(1f, 10), "A new generation must sample immediately.");
        Assert(!generationCadence.ShouldSample(1.01f, 10), "One generation must be throttled.");
        Assert(
            generationCadence.ShouldSample(1.01f, 11),
            "A replacement summary must bypass the old generation's throttle window."
        );
        Assert(
            generationCadence.ShouldSample(0.5f, 11),
            "A monotonic-clock reset must establish a fresh sample baseline."
        );
    }

    private static void Sampling_plan_reuses_and_invalidates_by_generation()
    {
        var cache = new EndOfRunReusablePlanCache<string, FakePlan>();
        var builds = 0;
        var first = GetOrCreate(
            cache,
            "card",
            generation: 1,
            plan => plan.Alive && plan.HierarchyGeneration == 1,
            () => new FakePlan(++builds, hierarchyGeneration: 1)
        );
        var reused = GetOrCreate(
            cache,
            "card",
            generation: 1,
            plan => plan.Alive && plan.HierarchyGeneration == 1,
            () => new FakePlan(++builds, hierarchyGeneration: 1)
        );
        Assert(ReferenceEquals(first, reused) && builds == 1, "A valid plan must be reused.");

        first.HierarchyGeneration = 2;
        var rebuiltForHierarchy = GetOrCreate(
            cache,
            "card",
            generation: 1,
            plan => plan.Alive && plan.HierarchyGeneration == 1,
            () => new FakePlan(++builds, hierarchyGeneration: 1)
        );
        Assert(
            !ReferenceEquals(first, rebuiltForHierarchy) && builds == 2,
            "A dynamic hierarchy generation must rebuild the cached traversal plan."
        );

        var rebuiltForCollection = GetOrCreate(
            cache,
            "card",
            generation: 2,
            plan => plan.Alive,
            () => new FakePlan(++builds, hierarchyGeneration: 1)
        );
        Assert(
            !ReferenceEquals(rebuiltForHierarchy, rebuiltForCollection) && builds == 3,
            "A loaded-card collection replacement must invalidate the old plan generation."
        );
    }

    private static void Sampling_plan_prunes_replaced_and_destroyed_entries()
    {
        var cache = new EndOfRunReusablePlanCache<string, FakePlan>();
        var live = GetOrCreate(cache, "live", 1, static _ => true, static () => new FakePlan(1, 1));
        var destroyed = GetOrCreate(
            cache,
            "destroyed",
            1,
            static _ => true,
            static () => new FakePlan(2, 1)
        );
        _ = GetOrCreate(cache, "replaced", 1, static _ => true, static () => new FakePlan(3, 1));
        destroyed.Alive = false;

        cache.Prune(1, new HashSet<string> { "live", "destroyed" }, plan => plan.Alive);

        Assert(cache.Count == 1 && live.Alive, "Destroyed and replacement plans must be pruned.");
    }

    private static void Excluded_hierarchy_sentinels_reject_equal_count_replacements()
    {
        var expected = new EndOfRunHierarchySentinelState(
            InstanceId: 20,
            ParentInstanceId: 10,
            SiblingIndex: 2,
            ChildCount: 3,
            IsExcluded: true
        );

        Assert(
            !EndOfRunHierarchySentinelCore.Matches(expected, expected with { InstanceId = 21 }),
            "Replacing an excluded root at the same sibling with the same child count must rebuild."
        );
        Assert(
            !EndOfRunHierarchySentinelCore.Matches(expected, expected with { ChildCount = 4 }),
            "A deep topology change below an excluded root must rebuild the sampling plan."
        );
        Assert(
            !EndOfRunHierarchySentinelCore.Matches(expected, expected with { IsExcluded = false }),
            "An excluded root becoming structural must enter the pose fingerprint."
        );
    }

    private static void Tooltip_snapshot_reuses_until_generation_or_lifetime_changes()
    {
        var cache = new NativeTooltipControllerCacheCore<FakeController>();
        var scans = 0;
        var first = new FakeController();
        FakeController[] Scan()
        {
            scans++;
            return scans == 1 ? [first] : [new FakeController()];
        }

        var initial = cache.GetOrRefresh(1, controller => controller.Alive, Scan);
        var reused = cache.GetOrRefresh(1, controller => controller.Alive, Scan);
        Assert(
            ReferenceEquals(initial, reused) && scans == 1,
            "Repeated clean-frame audits must reuse one controller snapshot."
        );

        first.Alive = false;
        _ = cache.GetOrRefresh(1, controller => controller.Alive, Scan);
        Assert(scans == 2, "A destroyed controller must invalidate and prune the snapshot.");

        _ = cache.GetOrRefresh(2, controller => controller.Alive, Scan);
        Assert(scans == 3, "A native topology generation change must rescan once.");
    }

    private static void Tooltip_lifecycle_signals_invalidate_equal_count_and_deep_topology_changes()
    {
        var topology = new NativeTooltipControllerTopologyGeneration();
        var cache = new NativeTooltipControllerCacheCore<FakeController>();
        var scans = 0;
        var controllers = new[] { new FakeController() };
        FakeController[] Scan()
        {
            scans++;
            return controllers;
        }

        _ = cache.GetOrRefresh(topology.Current, controller => controller.Alive, Scan);
        controllers = [new FakeController()];
        topology.ObserveControllerLifecycleChange();
        _ = cache.GetOrRefresh(topology.Current, controller => controller.Alive, Scan);
        Assert(
            scans == 2,
            "A same-count controller replacement must invalidate through its lifecycle signal."
        );

        controllers = [.. controllers, new FakeController()];
        topology.ObserveControllerLifecycleChange();
        _ = cache.GetOrRefresh(topology.Current, controller => controller.Alive, Scan);
        Assert(
            scans == 3,
            "A controller created below unchanged direct parent topology must invalidate the cache."
        );

        topology.ObserveControllerLifecycleChange();
        _ = cache.GetOrRefresh(topology.Current, controller => controller.Alive, Scan);
        Assert(scans == 4, "A further lifecycle signal must invalidate the controller snapshot.");
    }

    private static void Tooltip_audit_skips_only_inactive_non_authoritative_controllers()
    {
        Assert(
            DecideTooltipAudit(
                isAuthoritative: false,
                isActiveInHierarchy: false,
                hasRequiredSurface: false
            ) == NativeTooltipControllerAuditDecision.SkipInactiveNonAuthoritative,
            "A replay-stale inactive controller must not make clean-frame suppression unavailable."
        );
        Assert(
            DecideTooltipAudit(
                isAuthoritative: false,
                isActiveInHierarchy: false,
                hasRequiredSurface: true
            ) == NativeTooltipControllerAuditDecision.SkipInactiveNonAuthoritative,
            "Inactive non-authoritative controllers must be skipped regardless of stale surfaces."
        );
        Assert(
            DecideTooltipAudit(
                isAuthoritative: false,
                isActiveInHierarchy: true,
                hasRequiredSurface: true
            ) == NativeTooltipControllerAuditDecision.Audit,
            "An active controller must still be concealed and audited."
        );
        Assert(
            DecideTooltipAudit(
                isAuthoritative: false,
                isActiveInHierarchy: true,
                hasRequiredSurface: false
            ) == NativeTooltipControllerAuditDecision.Unavailable,
            "An active controller without its required surface must degrade capture."
        );
        Assert(
            DecideTooltipAudit(
                isAuthoritative: true,
                isActiveInHierarchy: false,
                hasRequiredSurface: true
            ) == NativeTooltipControllerAuditDecision.Audit,
            "The native parent's current controller must remain authoritative while inactive."
        );
        Assert(
            DecideTooltipAudit(
                isAuthoritative: true,
                isActiveInHierarchy: false,
                hasRequiredSurface: false
            ) == NativeTooltipControllerAuditDecision.Unavailable,
            "An authoritative controller without its required surface must degrade capture."
        );
    }

    private static NativeTooltipControllerAuditDecision DecideTooltipAudit(
        bool isAuthoritative,
        bool isActiveInHierarchy,
        bool hasRequiredSurface
    ) =>
        NativeTooltipControllerAuditCore.Decide(
            new NativeTooltipControllerAuditCandidate(
                isAuthoritative,
                isActiveInHierarchy,
                hasRequiredSurface
            )
        );

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static TPlan GetOrCreate<TKey, TPlan>(
        EndOfRunReusablePlanCache<TKey, TPlan> cache,
        TKey key,
        int generation,
        Func<TPlan, bool> isReusable,
        Func<TPlan> create
    )
        where TKey : notnull
    {
        if (cache.TryGet(key, generation, out var plan) && isReusable(plan))
            return plan;
        plan = create();
        cache.Set(key, generation, plan);
        return plan;
    }

    private sealed class FakePlan
    {
        internal FakePlan(int id, int hierarchyGeneration)
        {
            Id = id;
            HierarchyGeneration = hierarchyGeneration;
        }

        internal int Id { get; }
        internal int HierarchyGeneration { get; set; }
        internal bool Alive { get; set; } = true;
    }

    private sealed class FakeController
    {
        internal bool Alive { get; set; } = true;
    }
}
