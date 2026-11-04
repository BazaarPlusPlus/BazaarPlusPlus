#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>
/// The desktop's reads for Core's <see cref="EncounterPreviewModule"/>: the publisher's plans,
/// bound to the source they were published for, and the game state of
/// <see cref="IEncounterPreviewGameRuntime"/>. Templates show their own texts, which
/// <c>EventPreviewGameText</c> localizes.
/// </summary>
internal sealed class DesktopEncounterPreviewReads(
    EncounterPreviewPlanPublisher publisher,
    IEncounterPreviewGameRuntime runtime
) : IEncounterPreviewReads
{
    public bool IsInCombat => runtime.IsInCombat;

    public IEncounterPreviewPlans? TryGetPlans(out EventPreviewAvailability unavailable) =>
        publisher.TryGetSnapshot(out var source, out unavailable) is { } snapshot
            ? new Plans(snapshot, source, runtime)
            : null;

    public CardHero? ReadCurrentHero() => runtime.ReadCurrentHero();

    public EncounterInventory? ReadInventory() => runtime.ReadInventory();

    public string ColorKeywords(string text) => runtime.ColorKeywords(text);

    public EncounterPreviewTemplatePlan Present(EncounterPreviewTemplatePlan template) => template;

    private sealed class Plans(
        EncounterPreviewSnapshot snapshot,
        object source,
        IEncounterPreviewGameRuntime runtime
    ) : IEncounterPreviewPlans
    {
        public EncounterPreviewSnapshot Snapshot => snapshot;

        public (int? Day, GameDataDayTierTable? Table) ReadDayTiers()
        {
            var tiers = runtime.ResolveDayTiers(source);
            return (tiers.Day, tiers.Table);
        }

        public LiveAbilityValueResolver? LiveValues =>
            (
                Guid templateId,
                string effectId,
                bool isAura,
                out string valueText,
                out string? unit
            ) =>
                runtime.TryEvaluateAbilityValue(
                    source,
                    templateId,
                    effectId,
                    isAura,
                    out valueText,
                    out unit
                );
    }
}
