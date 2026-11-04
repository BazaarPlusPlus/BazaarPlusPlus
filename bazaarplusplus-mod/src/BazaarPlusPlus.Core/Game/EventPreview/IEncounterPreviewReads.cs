#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>
/// What <see cref="EncounterPreviewModule"/> reads from its host's game for one query.
/// </summary>
public interface IEncounterPreviewReads
{
    bool IsInCombat { get; }

    /// <summary>
    /// The current static-data source's plans; null while there are none, with
    /// <paramref name="unavailable"/> saying why (<see cref="EventPreviewAvailability.Loading"/> or
    /// <see cref="EventPreviewAvailability.Unavailable"/>).
    /// </summary>
    IEncounterPreviewPlans? TryGetPlans(out EventPreviewAvailability unavailable);

    /// <summary>The current hero; null when there is none, never <see cref="CardHero.Common"/>.</summary>
    CardHero? ReadCurrentHero();

    EncounterInventory? ReadInventory();

    /// <summary>The game's keyword colours over a line BPP writes.</summary>
    string ColorKeywords(string text);

    /// <summary>The template with the title and description the host shows for it.</summary>
    EncounterPreviewTemplatePlan Present(EncounterPreviewTemplatePlan template);
}

/// <summary>
/// One static-data source's plans, and the reads bound to that same source, so a query never
/// mixes a snapshot with another generation's day tiers or live values.
/// </summary>
public interface IEncounterPreviewPlans
{
    EncounterPreviewSnapshot Snapshot { get; }

    /// <summary>The run's day and the day's tier odds; either is null when unknown.</summary>
    (int? Day, GameDataDayTierTable? Table) ReadDayTiers();

    /// <summary>The live value of an ability placeholder; null when the host renders none.</summary>
    LiveAbilityValueResolver? LiveValues { get; }
}
