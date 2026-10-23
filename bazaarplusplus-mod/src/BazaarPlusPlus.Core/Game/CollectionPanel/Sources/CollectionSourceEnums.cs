#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

public enum CollectionSourceKind
{
    Merchant,
    Trainer,
}

public enum CollectionSourceHeroMode
{
    SelectedHero,
    AllHeroes,
    FixedHero,
    NeutralOnly,
    OtherHeroes,
}

public enum CollectionSourceStartingTierMode
{
    AtMost,
    Exact,
}

public enum CollectionSourceOfferSegmentKind
{
    Normal,
    Enchanted,
}

public enum CollectionSourceOfferPoolStatus
{
    Ready,
    NoneSelected,
}
