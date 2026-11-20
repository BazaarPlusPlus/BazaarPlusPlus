#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.EncounterPreview;

public readonly record struct EncounterCardPreviewQuery(
    Guid TemplateId,
    CardType Type,
    string NativeText
);

public readonly record struct LevelUpPreviewQuery(int CurrentLevel, CardHero? Hero = null);

public enum EncounterPreviewAvailability
{
    Available,
    Loading,
    Missing,
    Unsupported,
    Unavailable,
}

public readonly record struct EncounterPreviewResult(
    EncounterPreviewAvailability Availability,
    string? Content
);

public sealed class EncounterPreviewModule(BppSettings settings, IEncounterPreviewReads reads)
{
    private readonly BppSettings _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IEncounterPreviewReads _reads =
        reads ?? throw new ArgumentNullException(nameof(reads));

    public bool Enabled => _settings.EnableDetailedPreviewConfig.Value;

    public EncounterPreviewResult ResolveCard(EncounterCardPreviewQuery query)
    {
        try
        {
            if (!TryGetPlans(out var plans, out var unavailable))
                return Result(unavailable);
            if (string.IsNullOrEmpty(query.NativeText))
                return Result(EncounterPreviewAvailability.Unsupported);
            if (query.TemplateId == Guid.Empty)
                return Result(EncounterPreviewAvailability.Missing);
            return query.Type switch
            {
                CardType.EventEncounter => ResolveEvent(plans, query.TemplateId),
                CardType.EncounterStep => ResolveStep(plans, query),
                _ => Result(EncounterPreviewAvailability.Unsupported),
            };
        }
        catch (Exception)
        {
            return Result(EncounterPreviewAvailability.Unavailable);
        }
    }

    public EncounterPreviewResult ResolveLevelUp(LevelUpPreviewQuery query)
    {
        try
        {
            if (!TryGetPlans(out var plans, out var unavailable))
                return Result(unavailable);
            var snapshot = plans.Snapshot;
            if (!snapshot.TryGetLevelUp(query.CurrentLevel, out var levelUp))
                return Result(EncounterPreviewAvailability.Missing);

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
            return Result(EncounterPreviewAvailability.Unavailable);
        }
    }

    private EncounterPreviewResult ResolveEvent(IEncounterPreviewPlans plans, Guid templateId)
    {
        var snapshot = plans.Snapshot;
        if (!snapshot.TryGetEvent(templateId, out var plan))
            return Result(EncounterPreviewAvailability.Missing);

        var (day, table) = plans.ReadDayTiers();
        if (plan.Merchant is { } merchant)
            return merchant.IsSupported
                ? Presentation(
                    EncounterPreviewTextFormatter.BuildQualityLine(
                        merchant.UsesDayDistribution ? table : null,
                        merchant.FixedTier
                    )
                )
                : Result(EncounterPreviewAvailability.Unsupported);

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
            return Result(EncounterPreviewAvailability.Unsupported);
        return Presentation(
            EncounterPreviewTextFormatter.Build(option, _reads.ColorKeywords, table)
        );
    }

    private static EncounterPreviewResult ResolveStep(
        IEncounterPreviewPlans plans,
        EncounterCardPreviewQuery query
    )
    {
        if (!plans.Snapshot.TryGetTemplate(query.TemplateId, out var step))
            return Result(EncounterPreviewAvailability.Missing);
        if (step.Kind != EncounterPreviewTemplateKind.EncounterStep)
            return Result(EncounterPreviewAvailability.Unsupported);

        return Presentation(
            EncounterPreviewTextFormatter.BuildRewardQualityLine(
                step.RewardFilter,
                query.NativeText,
                plans.ReadDayTiers().Table
            )
        );
    }

    private bool TryGetPlans(
        out IEncounterPreviewPlans plans,
        out EncounterPreviewAvailability unavailable
    )
    {
        plans = null!;
        if (!Enabled || _reads.IsInCombat)
        {
            unavailable = EncounterPreviewAvailability.Unsupported;
            return false;
        }

        if (_reads.TryGetPlans(out unavailable) is not { } current)
            return false;
        plans = current;
        return true;
    }

    private static EncounterPreviewResult Presentation(string? content) =>
        string.IsNullOrWhiteSpace(content)
            ? Result(EncounterPreviewAvailability.Unsupported)
            : new EncounterPreviewResult(EncounterPreviewAvailability.Available, content);

    private static EncounterPreviewResult Result(EncounterPreviewAvailability availability) =>
        new(availability, null);
}
