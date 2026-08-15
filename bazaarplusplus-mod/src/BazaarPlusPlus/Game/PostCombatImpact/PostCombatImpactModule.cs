#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.GameInterop.Events;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal interface IPostCombatImpactModule
{
    bool TryGetSource(string instanceId, out CombatImpactSource source);

    bool TryGetReceived(string instanceId, out CombatImpactReceived received);

    void SetHoveredRecapCard(
        RecapItemVisualController recapVisual,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    );

    void ClearHoveredRecapCard(
        RecapItemVisualController recapVisual,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    );

    void SetHoveredSkill(
        SkillProxyRenderer skill,
        Card card,
        CardTooltipData? tooltipData,
        Vector3 tooltipOffset
    );

    void ClearHoveredSkill(
        SkillProxyRenderer skill,
        PostCombatImpactHoverExitOrigin origin,
        bool nativeTooltipLocked
    );

    void OnNativeTooltipPreparing(CardTooltipController controller, ITooltipData tooltipData);

    void OnNativeTooltipChanging(CardTooltipController controller);

    void OnNativeTooltipInteractabilityChanged(
        BaseTooltipController controller,
        CanvasGroup canvasGroup
    );

    void OnNativeAuxiliaryTooltipShowing(
        AuxiliaryTooltipController controller,
        Transform anchor,
        string header,
        string body
    );

    void OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller);
}

internal sealed class PostCombatImpactModule : IBppFeature, IPostCombatImpactModule
{
    private readonly IBppEventBus _eventBus;
    private readonly BppStaticCardMapProvider _cardMapProvider;
    private readonly CombatImpactReportRegistry _reports = new();
    private IDisposable? _subscription;
    private PostCombatImpactController? _runtime;

    internal PostCombatImpactModule(IBppEventBus eventBus, BppStaticCardMapProvider cardMapProvider)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
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
        _subscription = _eventBus.Subscribe<CombatSimObserved>(OnCombatSimObserved);
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _reports.Reset();
        _runtime?.HideDetails();
    }

    private void OnCombatSimObserved(CombatSimObserved observed)
    {
        var generation = _reports.BeginGeneration();
        var simulation = observed.Message.Data;
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
                    CombatImpactImplicitPlayerEffectReader.AddMissing(
                        simulation,
                        detachedEntities,
                        cardMap.Values.OfType<TCardBase>()
                    );
                }
            }

            var report = await Task.Run(() =>
                    CombatImpactProjector.Project(simulation, detachedEntities)
                )
                .ConfigureAwait(false);
            if (!_reports.TryPublish(generation, report))
                return;

            foreach (var kind in report.ProjectionDiagnostics.Select(item => item.Kind).Distinct())
            {
                BppLog.WarnEvent(
                    PostCombatImpactLogEvents.ProjectionDegraded,
                    PostCombatImpactLogEvents.ReasonCode.Bind(
                        PostCombatImpactReasonCode.ProjectionAttributionGap
                    ),
                    PostCombatImpactLogEvents.ProjectionCategory.Bind(kind)
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
            PostCombatImpactLogEvents.ProjectionDegraded,
            ex,
            PostCombatImpactLogEvents.ReasonCode.Bind(
                PostCombatImpactReasonCode.ProjectionException
            ),
            PostCombatImpactLogEvents.ProjectionCategory.Bind(
                PostCombatImpactReasonCode.ProjectionException
            )
        );
    }
}
