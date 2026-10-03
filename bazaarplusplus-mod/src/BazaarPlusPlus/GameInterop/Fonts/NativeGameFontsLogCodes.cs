#nullable enable

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
