#nullable enable

namespace BazaarPlusPlus.Game.Supporters;

public readonly record struct SupporterCatalogFailure(
    SupporterCatalogSource Source,
    SupporterLogReasonCode Reason
);
