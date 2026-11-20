using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Mobile.Collection;

internal sealed record TouchCollectionData(
    ICollectionCardCatalog Catalog,
    ICollectionSourceCatalog Sources,
    ICollectionDayTierSource DayTiers,
    ICollectionPanelHeroPreferenceStore HeroPreference,
    ICollectionRunReads Run,
    IEncounterStateProbe Encounters,
    ISupporterRotation? Supporters
);
