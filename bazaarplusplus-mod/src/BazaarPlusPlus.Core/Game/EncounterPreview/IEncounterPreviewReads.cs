#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.EncounterPreview;

public interface IEncounterPreviewReads
{
    bool IsInCombat { get; }

    IEncounterPreviewPlans? TryGetPlans(out EncounterPreviewAvailability unavailable);

    /// <summary>Null means there is no current hero. Common is a card scope.</summary>
    CardHero? ReadCurrentHero();

    EncounterInventory? ReadInventory();

    string ColorKeywords(string text);

    EncounterPreviewTemplatePlan Present(EncounterPreviewTemplatePlan template);
}

/// <summary>Bind day tiers and live values to the same static-data generation as the plans.</summary>
public interface IEncounterPreviewPlans
{
    EncounterPreviewSnapshot Snapshot { get; }

    (int? Day, GameDataDayTierTable? Table) ReadDayTiers();

    LiveAbilityValueResolver? LiveValues { get; }
}
