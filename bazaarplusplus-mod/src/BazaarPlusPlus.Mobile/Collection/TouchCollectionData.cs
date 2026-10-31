using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Mobile.Collection;

/// <summary>
/// The host services the touch Collection reads, each a Core port the host's composition binds
/// (ADR-0025 decision 4).
/// </summary>
/// <param name="Catalog">The card catalog, built from the host's game data.</param>
/// <param name="Sources">The merchant and trainer offer pools.</param>
/// <param name="DayTiers">The run day and, where the host reads it, the day's highest tier.</param>
/// <param name="HeroPreference">The remembered out-of-run hero.</param>
/// <param name="Run">The run, its day and its hero, when the panel opens.</param>
/// <param name="Encounters">The encounter ids that pre-select a source; none where the host reads none.</param>
/// <param name="Supporters">The supporter credit's rotation, or null where the host has none.</param>
internal sealed record TouchCollectionData(
    ICollectionCardCatalog Catalog,
    ICollectionSourceCatalog Sources,
    ICollectionDayTierSource DayTiers,
    ICollectionPanelHeroPreferenceStore HeroPreference,
    ICollectionRunReads Run,
    IEncounterStateProbe Encounters,
    ISupporterRotation? Supporters
);
