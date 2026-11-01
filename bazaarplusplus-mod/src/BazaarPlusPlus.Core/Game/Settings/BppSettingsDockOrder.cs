#nullable enable

namespace BazaarPlusPlus.Game.Settings;

public static class BppSettingsDockOrder
{
    public const int NameOverride = 0;
    public const int FixedSupporterList = 1;
    public const int BilingualItemNames = 2;
    public const int DetailedPreview = 3;
    public const int EndOfRunScreenshot = 5;
    public const int BazaarDbUpload = 6;
    public const int CosmeticNames = 7;
    public const int CombatStatusBar = 8;
    public const int EnchantPreview = 9;
    public const int UpgradePreviewActivation = 10;
    public const int LegendaryPosition = 11;
    public const int ChineseLocaleMode = 12;
    public const int VoiceSubtitles = 13;
    public const int VoiceSubtitlesPosition = 14;
    public const int VoiceSubtitlesEnglishFontScale = 15;
    public const int VoiceSubtitlesChineseFontScale = 16;
    public const int GameHistory = 17;
    public const int GraphicsUpscaling = 18;
    public const int GraphicsUpscalingSharpness = 19;

    // The Android host's own rows: the upgrade preview switch that stands in for the desktop's
    // hotkey, the game-update row (a newer game, or a new Android App), and the Compatibility
    // Mode notice.
    public const int UpgradePreviewSwitch = 20;
    public const int GameUpdate = 21;
    public const int CompatibilityMode = 22;
}
