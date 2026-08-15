#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionCatalogWarmupStatus
{
    WaitingForStaticData,
    LoadingCardMap,
    Building,
    Ready,
    Unavailable,
}
