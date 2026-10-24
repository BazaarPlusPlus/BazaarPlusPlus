#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
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
        if (simulation == null)
            return;

        IReadOnlyDictionary<string, CombatImpactEntity> entities;
        try
        {
            // This is the only Unity/game-runtime read. The expensive replay projection below
            // operates only on the detached snapshot and immutable combat message data.
            entities = CombatImpactEntitySnapshotReader.Read(simulation);
        }
        catch (Exception ex)
        {
            ReportProjectionException(ex);
            return;
        }

        var cardMapLoad = _cardMapProvider.BeginLoad(out _);
        _ = ProjectInBackgroundAsync(simulation, entities, cardMapLoad, generation);
    }

    private async Task ProjectInBackgroundAsync(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        Task<Dictionary<Guid, ITCard>?>? cardMapLoad,
        long generation
    )
    {
        try
        {
            // The producer reads Core's combat timeline (ADR-0019); the copy runs off the game
            // thread, as the projection does.
            var timeline = await Task.Run(() => DesktopCombatTimeline.From(simulation))
                .ConfigureAwait(false);
            var detachedEntities = new Dictionary<string, CombatImpactEntity>(
                entities,
                StringComparer.Ordinal
            );
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
                    // Runs on the thread pool after the first await, so AddMissing must read only
                    // static template data and never touch Unity objects.
                    CombatImpactImplicitPlayerEffectReader.AddMissing(
                        timeline,
                        detachedEntities,
                        cardMap.Values.OfType<TCardBase>()
                    );
                }
            }

            var report = await Task.Run(() =>
                    CombatImpactProjector.Project(timeline, detachedEntities)
                )
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
