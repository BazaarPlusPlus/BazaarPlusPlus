#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.GameInterop.Fonts;

internal enum NativeGameFontReasonCode
{
    ConfigurationUnavailable,
    FontReferencesUnavailable,
    FontLoadFailed,
    SourceFontUnavailable,
    SourceFontNotDynamic,
    PanelTextSettingsUnavailable,
}

internal enum NativeGameFontStage
{
    ResolveConfiguration,
    LoadFonts,
    ResolveSourceFont,
    ConfigurePanelTextSettings,
    RestoreBinding,
    ReleaseHandle,
}

[BppLogEventSource]
internal static class NativeGameFontsLogEvents
{
    internal static readonly BppLogFieldDefinition DegradedStage = PublicLow(0, "stage");
    internal static readonly BppLogFieldDefinition DegradedReasonCode = PublicLow(1, "reason_code");
    internal static readonly BppLogEventDefinition Degraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.native_game_fonts.degraded",
        [DegradedStage, DegradedReasonCode],
        new BppLogStormPolicy([DegradedStage, DegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition LoadedFontCount = PublicHigh(0, "font_count");
    internal static readonly BppLogFieldDefinition LoadedFontNames = Untrusted(1, "font_names");
    internal static readonly BppLogFieldDefinition LoadedSourceFont = Untrusted(2, "source_font");
    internal static readonly BppLogEventDefinition Loaded = new(
        BppLogFeatureScope.Plugin,
        "plugin.native_game_fonts.loaded",
        [LoadedFontCount, LoadedFontNames, LoadedSourceFont]
    );

    internal static readonly BppLogFieldDefinition RecoveredFontCount = PublicHigh(0, "font_count");
    internal static readonly BppLogEventDefinition Recovered = new(
        BppLogFeatureScope.Plugin,
        "plugin.native_game_fonts.recovered",
        [RecoveredFontCount]
    );

    internal static readonly BppLogFieldDefinition CleanupFailedStage = PublicLow(0, "stage");
    internal static readonly BppLogEventDefinition CleanupFailed = new(
        BppLogFeatureScope.Plugin,
        "plugin.native_game_fonts.cleanup_failed",
        [CleanupFailedStage]
    );

    internal static readonly BppLogFieldDefinition TextRejectedSurface = PublicLow(0, "surface");
    internal static readonly BppLogFieldDefinition TextRejectedCodePoint = PublicHigh(
        1,
        "code_point"
    );
    internal static readonly BppLogEventDefinition TextRejected = new(
        BppLogFeatureScope.Plugin,
        "plugin.native_game_fonts.text_rejected",
        [TextRejectedSurface, TextRejectedCodePoint],
        new BppLogStormPolicy([TextRejectedSurface])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );

    private static BppLogFieldDefinition PublicHigh(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );

    private static BppLogFieldDefinition Untrusted(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.UntrustedText,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
