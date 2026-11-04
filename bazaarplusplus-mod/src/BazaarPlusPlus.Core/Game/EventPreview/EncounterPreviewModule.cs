#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>An inspected card: its template, its type, and the native passive text it shows.</summary>
public readonly record struct EncounterCardPreviewQuery(
    Guid TemplateId,
    CardType Type,
    string NativeText
);

/// <summary>
/// A hero-level badge: the level it shows, and the hero whose rewards it lists; null reads the
/// current hero.
/// </summary>
public readonly record struct LevelUpPreviewQuery(int CurrentLevel, CardHero? Hero = null);

public enum EventPreviewAvailability
{
    Available,
    Loading,
    Missing,
    Unsupported,
    Unavailable,
}

public readonly record struct EventPreviewResult(
    EventPreviewAvailability Availability,
    string? Content
);

/// <summary>
/// The Encounter Preview Module both hosts query: the Detailed Preview switch and the combat gate,
/// then by card type an event's choices or outcomes, a merchant's quality, or an encounter step's
/// reward odds, and a hero level's rewards. Game state comes through
/// <see cref="IEncounterPreviewReads"/>.
/// </summary>
public sealed class EncounterPreviewModule(BppSettings settings, IEncounterPreviewReads reads)
{
    private readonly BppSettings _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IEncounterPreviewReads _reads =
        reads ?? throw new ArgumentNullException(nameof(reads));

    /// <summary>The Detailed Preview switch.</summary>
    public bool Enabled => _settings.EnableDetailedPreviewConfig.Value;

    public EventPreviewResult ResolveCard(EncounterCardPreviewQuery query)
    {
        try
        {
            if (!TryGetPlans(out var plans, out var unavailable))
                return Result(unavailable);
            if (string.IsNullOrEmpty(query.NativeText))
                return Result(EventPreviewAvailability.Unsupported);
            if (query.TemplateId == Guid.Empty)
                return Result(EventPreviewAvailability.Missing);
            return query.Type switch
            {
                CardType.EventEncounter => ResolveEvent(plans, query.TemplateId),
                CardType.EncounterStep => ResolveStep(plans, query),
                _ => Result(EventPreviewAvailability.Unsupported),
            };
        }
        catch (Exception)
        {
            return Result(EventPreviewAvailability.Unavailable);
        }
    }

    public EventPreviewResult ResolveLevelUp(LevelUpPreviewQuery query)
    {
        try
        {
            if (!TryGetPlans(out var plans, out var unavailable))
                return Result(unavailable);
            var snapshot = plans.Snapshot;
            if (!snapshot.TryGetLevelUp(query.CurrentLevel, out var levelUp))
                return Result(EventPreviewAvailability.Missing);

            return Presentation(
                LevelUpPreviewTextFormatter.Build(
                    levelUp,
                    id =>
                        snapshot.TryGetTemplate(id, out var template)
                            ? _reads.Present(template)
                            : null,
                    query.Hero ?? _reads.ReadCurrentHero(),
                    _reads.ColorKeywords,
                    query.CurrentLevel,
                    plans.LiveValues
                )
            );
        }
        catch (Exception)
        {
            return Result(EventPreviewAvailability.Unavailable);
        }
    }

    private EventPreviewResult ResolveEvent(IEncounterPreviewPlans plans, Guid templateId)
    {
        var snapshot = plans.Snapshot;
        if (!snapshot.TryGetEvent(templateId, out var plan))
            return Result(EventPreviewAvailability.Missing);

        var (day, table) = plans.ReadDayTiers();
        if (plan.Merchant is { } merchant)
            return merchant.IsSupported
                ? Presentation(
                    EncounterPreviewTextFormatter.BuildQualityLine(
                        merchant.UsesDayDistribution ? table : null,
                        merchant.FixedTier
                    )
                )
                : Result(EventPreviewAvailability.Unsupported);

        // The event and the templates it names, as the host presents them.
        var templates = new List<EncounterPreviewTemplatePlan>();
        foreach (
            var id in plan
                .ChoiceGroups.SelectMany(group => group.Members.Select(member => member.TemplateId))
                .Concat(plan.OutcomeGroups.SelectMany(group => group.Ids))
                .Append(templateId)
                .Distinct()
        )
            if (snapshot.TryGetTemplate(id, out var template))
                templates.Add(_reads.Present(template));

        var option = EncounterEventDetailResolver.TryResolve(
            plan,
            new EncounterPreviewSnapshot([plan], templates),
            _reads.ReadCurrentHero(),
            _reads.ReadInventory(),
            day,
            plans.LiveValues
        );
        if (option == null || (!option.HasChoiceDetails && !option.HasOutcomeGroups))
            return Result(EventPreviewAvailability.Unsupported);
        return Presentation(
            EncounterPreviewTextFormatter.Build(option, _reads.ColorKeywords, table)
        );
    }

    private static EventPreviewResult ResolveStep(
        IEncounterPreviewPlans plans,
        EncounterCardPreviewQuery query
    )
    {
        if (!plans.Snapshot.TryGetTemplate(query.TemplateId, out var step))
            return Result(EventPreviewAvailability.Missing);
        if (step.Kind != EncounterPreviewTemplateKind.EncounterStep)
            return Result(EventPreviewAvailability.Unsupported);

        return Presentation(
            EncounterPreviewTextFormatter.BuildRewardQualityLine(
                step.RewardFilter,
                query.NativeText,
                plans.ReadDayTiers().Table
            )
        );
    }

    // The switch and a fight come first: either leaves the native tooltip as it is.
    private bool TryGetPlans(
        out IEncounterPreviewPlans plans,
        out EventPreviewAvailability unavailable
    )
    {
        plans = null!;
        if (!Enabled || _reads.IsInCombat)
        {
            unavailable = EventPreviewAvailability.Unsupported;
            return false;
        }

        if (_reads.TryGetPlans(out unavailable) is not { } current)
            return false;
        plans = current;
        return true;
    }

    private static EventPreviewResult Presentation(string? content) =>
        string.IsNullOrWhiteSpace(content)
            ? Result(EventPreviewAvailability.Unsupported)
            : new EventPreviewResult(EventPreviewAvailability.Available, content);

    private static EventPreviewResult Result(EventPreviewAvailability availability) =>
        new(availability, null);
}
