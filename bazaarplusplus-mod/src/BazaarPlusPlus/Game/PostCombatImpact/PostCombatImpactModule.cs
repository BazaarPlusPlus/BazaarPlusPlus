#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.GameInterop.CombatSimulation;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal sealed class PostCombatImpactModule : IBppFeature
{
    private readonly BppStaticCardMapProvider _cardMapProvider;
    private readonly CombatImpactReportRegistry _reports = new();
    private bool _started;

    // The newest projection generation, written and read only on the game thread. A projection
    // whose generation is no longer this one, or that resumes after Stop, skips its remaining
    // game-thread work: TryPublish would discard its report anyway.
    private long _gameThreadGeneration;
    private PostCombatImpactController? _runtime;

    internal PostCombatImpactModule(BppStaticCardMapProvider cardMapProvider)
    {
        _cardMapProvider =
            cardMapProvider ?? throw new ArgumentNullException(nameof(cardMapProvider));
    }

    internal CombatImpactReport LatestReport => _reports.Latest;

    internal void AttachRuntime(PostCombatImpactController runtime)
    {
        _runtime = runtime;
    }

    internal void DetachRuntime(PostCombatImpactController runtime)
    {
        if (ReferenceEquals(_runtime, runtime))
            _runtime = null;
    }

    public bool TryGetSource(string instanceId, out CombatImpactSource source)
    {
        var match = LatestReport.Sources.FirstOrDefault(candidate =>
            string.Equals(candidate.Entity.Id, instanceId, StringComparison.Ordinal)
        );
        if (match == null)
        {
            source = null!;
            return false;
        }

        source = match;
        return true;
    }

    public bool TryGetReceived(string instanceId, out CombatImpactReceived received)
    {
        var match = LatestReport.Received.FirstOrDefault(candidate =>
            string.Equals(candidate.Entity.Id, instanceId, StringComparison.Ordinal)
        );
        if (match == null)
        {
            received = null!;
            return false;
        }

        received = match;
        return true;
    }

    public void SetHoveredRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) => _runtime?.SetHoveredRecapCard(recapVisual, card, tooltipData, tooltipOffset);

    public void ClearHoveredRecapCard(
        RecapItemVisualController recapVisual,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    ) => _runtime?.ClearHoveredRecapCard(recapVisual, origin, nativeTooltipLocked);

    public void SetHoveredSkill(
        SkillProxyRenderer skill,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    ) => _runtime?.SetHoveredSkill(skill, card, tooltipData, tooltipOffset);

    public void ClearHoveredSkill(
        SkillProxyRenderer skill,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    ) => _runtime?.ClearHoveredSkill(skill, origin, nativeTooltipLocked);

    public void OnNativeTooltipPreparing(
        CardTooltipController controller,
        ITooltipData tooltipData
    ) => _runtime?.OnNativeTooltipPreparing(controller, tooltipData);

    public void OnNativeTooltipChanging(CardTooltipController controller) =>
        _runtime?.OnNativeTooltipChanging(controller);

    public void OnNativeTooltipInteractabilityChanged(
        BaseTooltipController controller,
        CanvasGroup canvasGroup
    ) => _runtime?.OnNativeTooltipInteractabilityChanged(controller, canvasGroup);

    public void OnNativeAuxiliaryTooltipShowing(
        AuxiliaryTooltipController controller,
        Transform anchor,
        string header,
        string body
    ) => _runtime?.OnNativeAuxiliaryTooltipShowing(controller, anchor, header, body);

    public void OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller) =>
        _runtime?.OnNativeAuxiliaryTooltipHiding(controller);

    public void Start()
    {
        _started = true;
    }

    public void Stop()
    {
        _started = false;
        _gameThreadGeneration = 0;
        _reports.Reset();
        _runtime?.HideDetails();
    }

    /// <summary>
    /// Called by the combat-simulation patch on the game thread before the simulation plays. The
    /// projection needs the whole simulation DTO, so this host feature takes it directly instead
    /// of through the Core event bus, which carries only <c>CombatSimSnapshot</c>.
    /// </summary>
    internal void ObserveCombatSimulation(CombatSim? simulation)
    {
        if (!_started)
            return;

        try
        {
            BeginProjection(simulation);
        }
        catch (Exception ex)
        {
            // A Harmony prefix must never throw into the game's simulation call.
            ReportProjectionException(ex);
        }
    }

    private void BeginProjection(CombatSim? simulation)
    {
        var generation = _reports.BeginGeneration();
        _gameThreadGeneration = generation;
        if (simulation == null)
            return;

        CombatImpactRuntimeEntities runtimeEntities;
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // The live cards and players are mutable runtime objects, so they are copied here,
            // before the simulation plays. Every pass over the simulation's frames runs later,
            // off the game thread.
            runtimeEntities = CombatImpactEntitySnapshotReader.ReadRuntime();
        }
        catch (Exception ex)
        {
            ReportProjectionException(ex);
            return;
        }
        LogProjectionStep("runtime_read", startedAt, runtimeEntities.Entities.Count);

        var cardMapLoad = _cardMapProvider.BeginLoad(out _);
        _ = ProjectInBackgroundAsync(simulation, runtimeEntities, cardMapLoad, generation);
    }

    // Started on the game thread by the combat-simulation prefix. The first await deliberately
    // keeps Unity's synchronization context, so transformed cards are rehydrated back on the game
    // thread, as LiveBuildPanel.RefreshFinalBuildsAsync relies on
    // (src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs).
    private async Task ProjectInBackgroundAsync(
        CombatSim simulation,
        CombatImpactRuntimeEntities runtimeEntities,
        Task<Dictionary<Guid, ITCard>?>? cardMapLoad,
        long generation
    )
    {
        try
        {
            // Both passes read only the immutable combat message. The producer reads Core's
            // combat timeline (ADR-0019). The transformed-card scan finds the cards a transform
            // created during combat, which Data.Entities does not hold.
            var scan = await Task.Run(() =>
                (
                    Timeline: DesktopCombatTimeline.From(simulation),
                    TransformedCards: CombatImpactTransformedCardSnapshotReader.Read(simulation)
                )
            );

            // Back on the game thread, which CombatImpactEntitySnapshotReader.Complete requires.
            if (!_started || generation != _gameThreadGeneration)
                return;
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var entities = CombatImpactEntitySnapshotReader.Complete(
                runtimeEntities,
                scan.TransformedCards
            );
            LogProjectionStep("transformed_read", startedAt, scan.TransformedCards.Count);

            var report = await Task.Run(() => ProjectAsync(scan.Timeline, entities, cardMapLoad))
                .ConfigureAwait(false);
            if (!_reports.TryPublish(generation, report))
                return;

            foreach (var kind in report.ProjectionDiagnostics.Select(item => item.Kind).Distinct())
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.PostCombatImpact,
                        "post_combat_impact.projection.degraded",
                        storm: ["reason_code", "projection_category"]
                    ),
                    ("reason_code", PostCombatImpactReasonCode.ProjectionAttributionGap),
                    ("projection_category", kind)
                );
            }
        }
        catch (Exception ex)
        {
            if (_reports.TryPublish(generation, CombatImpactReport.Empty))
                ReportProjectionException(ex);
        }
    }

    // Runs on the thread pool, so AddMissing must read only static template data and never touch
    // Unity objects.
    private static async Task<CombatImpactReport> ProjectAsync(
        CombatTimeline timeline,
        Dictionary<string, CombatImpactEntity> entities,
        Task<Dictionary<Guid, ITCard>?>? cardMapLoad
    )
    {
        if (cardMapLoad != null)
        {
            Dictionary<Guid, ITCard>? cardMap;
            try
            {
                cardMap = await cardMapLoad.ConfigureAwait(false);
            }
            catch
            {
                // Static-data enrichment is optional. Keep the ordinary projection available
                // if the shared card-map warmup failed for an unrelated feature.
                cardMap = null;
            }
            if (cardMap != null)
            {
                CombatImpactImplicitPlayerEffectReader.AddMissing(
                    timeline,
                    entities,
                    cardMap.Values.OfType<TCardBase>()
                );
            }
        }

        return CombatImpactProjector.Project(timeline, entities);
    }

    // Temporary timing probe for issue #528: Debug builds time the two game-thread steps of a
    // projection, so a profiler-free run can read the combat-start cost from LogOutput.log. Delete
    // it in the commit that decides P-22 step 2 (moving template analysis off the game thread).
    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogProjectionStep(string step, long startedAt, int entityCount)
    {
        var elapsedMs =
            (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
            * 1000d
            / System.Diagnostics.Stopwatch.Frequency;
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.PostCombatImpact,
                "post_combat_impact.projection.game_thread_step"
            ),
            () => [("step", step), ("entity_count", entityCount), ("elapsed_ms", elapsedMs)]
        );
    }

    private static void ReportProjectionException(Exception ex)
    {
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.PostCombatImpact,
                "post_combat_impact.projection.degraded",
                storm: ["reason_code", "projection_category"]
            ),
            ex,
            ("reason_code", PostCombatImpactReasonCode.ProjectionException),
            ("projection_category", PostCombatImpactReasonCode.ProjectionException)
        );
    }
}
