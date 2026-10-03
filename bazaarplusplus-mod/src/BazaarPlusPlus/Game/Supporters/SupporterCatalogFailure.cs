#nullable enable

namespace BazaarPlusPlus.Game.Supporters;

internal readonly record struct SupporterCatalogFailure(
    SupporterCatalogSource Source,
    SupporterLogReasonCode Reason
);
