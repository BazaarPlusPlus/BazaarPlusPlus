#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.GameInterop.StaticCards;

[BppLogEventSource]
internal static class StaticCardsLogEvents
{
    internal static readonly BppLogFieldDefinition AcceptedCount = new(
        0,
        "accepted_count",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition UnsupportedCount = new(
        1,
        "unsupported_count",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition UnsupportedTemplates = new(
        BppLogFeatureScope.StaticCards,
        "static_cards.catalog.unsupported_templates",
        [AcceptedCount, UnsupportedCount]
    );
}
