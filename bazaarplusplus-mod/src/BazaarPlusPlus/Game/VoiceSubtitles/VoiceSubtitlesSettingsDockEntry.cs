#nullable enable
using System.Globalization;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceSubtitlesSettingsDockEntry
{
    private static readonly LocalizedTextSet Off = new("OFF", "关闭", "關閉");
    private static readonly LocalizedTextSet Both = new("BOTH", "双语", "雙語");
    private static readonly LocalizedTextSet Chinese = new("ZH", "中文", "中文");
    private static readonly LocalizedTextSet English = new("EN", "英文", "英文");

    internal static CyclingSettingsDockEntry<SubtitleMode> Create() =>
        new(
            BppSettingsDockOrder.VoiceSubtitles,
            "VoiceSubtitles",
            VoiceSubtitlesSettingsMenuLabel.Resolve,
            new[]
            {
                SubtitleMode.Off,
                SubtitleMode.Both,
                SubtitleMode.Chinese,
                SubtitleMode.English,
            },
            ReadMode,
            WriteMode,
            mode => mode != SubtitleMode.Off,
            ResolveStatus
        );

    internal static void RegisterAll(SettingsDockEntryRegistry registry)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        registry.Register(Create());
        registry.Register(VoiceSubtitlesPositionSettingsDockEntry.Create());
        registry.Register(VoiceSubtitlesEnglishFontScaleSettingsDockEntry.Create());
        registry.Register(VoiceSubtitlesChineseFontScaleSettingsDockEntry.Create());
    }

    private static SubtitleMode ReadMode(IBppConfig config)
    {
        if (config.EnableVoiceSubtitlesConfig?.Value != true)
            return SubtitleMode.Off;

        return (config.VoiceSubtitlesLanguageModeConfig?.Value ?? SubtitleLanguageMode.Both) switch
        {
            SubtitleLanguageMode.ChineseOnly => SubtitleMode.Chinese,
            SubtitleLanguageMode.EnglishOnly => SubtitleMode.English,
            _ => SubtitleMode.Both,
        };
    }

    private static void WriteMode(IBppConfig config, SubtitleMode mode)
    {
        var enabledEntry = config.EnableVoiceSubtitlesConfig;
        if (enabledEntry != null)
            enabledEntry.Value = mode != SubtitleMode.Off;

        var languageEntry = config.VoiceSubtitlesLanguageModeConfig;
        if (languageEntry != null)
        {
            languageEntry.Value = mode switch
            {
                SubtitleMode.Chinese => SubtitleLanguageMode.ChineseOnly,
                SubtitleMode.English => SubtitleLanguageMode.EnglishOnly,
                _ => SubtitleLanguageMode.Both,
            };
        }
    }

    private static string ResolveStatus(SubtitleMode mode, string languageCode)
    {
        var text = mode switch
        {
            SubtitleMode.Both => Both,
            SubtitleMode.Chinese => Chinese,
            SubtitleMode.English => English,
            _ => Off,
        };
        return text.Resolve(languageCode, L.CurrentMode);
    }

    internal enum SubtitleMode
    {
        Off,
        Both,
        Chinese,
        English,
    }
}

internal static class VoiceSubtitlesPositionSettingsDockEntry
{
    private static readonly LocalizedTextSet Label = new(
        "Subtitle Position",
        "字幕位置",
        "字幕位置"
    );
    private static readonly LocalizedTextSet TopLeft = new("Top Left", "左上", "左上");
    private static readonly LocalizedTextSet TopRight = new("Top Right", "右上", "右上");
    private static readonly LocalizedTextSet TopCenter = new("Top Center", "顶部居中", "頂部置中");

    internal static CyclingSettingsDockEntry<SubtitlePosition> Create() =>
        new(
            BppSettingsDockOrder.VoiceSubtitlesPosition,
            "VoiceSubtitlesPosition",
            ResolveLabel,
            new[]
            {
                SubtitlePosition.TopLeft,
                SubtitlePosition.TopRight,
                SubtitlePosition.TopCenter,
            },
            config =>
                config.VoiceSubtitlesPositionConfig?.Value
                ?? BppConfig.DefaultVoiceSubtitlesPosition,
            (config, position) =>
            {
                var entry = config.VoiceSubtitlesPositionConfig;
                if (entry != null)
                    entry.Value = position;
            },
            position => position != BppConfig.DefaultVoiceSubtitlesPosition,
            ResolveStatus
        );

    private static string ResolveLabel(string languageCode) => Resolve(Label, languageCode);

    private static string ResolveStatus(SubtitlePosition position, string languageCode)
    {
        return Resolve(
            position switch
            {
                SubtitlePosition.TopRight => TopRight,
                SubtitlePosition.TopCenter => TopCenter,
                _ => TopLeft,
            },
            languageCode
        );
    }

    private static string Resolve(LocalizedTextSet text, string languageCode) =>
        text.Resolve(languageCode, L.CurrentMode);
}

internal static class VoiceSubtitlesEnglishFontScaleSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<float> Create() =>
        VoiceSubtitlesFontScaleDockEntryFactory.Create(
            BppSettingsDockOrder.VoiceSubtitlesEnglishFontScale,
            "VoiceSubtitlesEnglishFontScale",
            new LocalizedTextSet("English Size", "英文字号", "英文字號"),
            config => config.VoiceSubtitlesEnglishFontScaleConfig?.Value ?? 1f,
            (config, scale) =>
            {
                var entry = config.VoiceSubtitlesEnglishFontScaleConfig;
                if (entry != null)
                    entry.Value = scale;
            }
        );
}

internal static class VoiceSubtitlesChineseFontScaleSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<float> Create() =>
        VoiceSubtitlesFontScaleDockEntryFactory.Create(
            BppSettingsDockOrder.VoiceSubtitlesChineseFontScale,
            "VoiceSubtitlesChineseFontScale",
            new LocalizedTextSet("Chinese Size", "中文字号", "中文字號"),
            config => config.VoiceSubtitlesChineseFontScaleConfig?.Value ?? 1f,
            (config, scale) =>
            {
                var entry = config.VoiceSubtitlesChineseFontScaleConfig;
                if (entry != null)
                    entry.Value = scale;
            }
        );
}

internal static class VoiceSubtitlesFontScaleDockEntryFactory
{
    private const float ComparisonTolerance = 0.0001f;
    private static readonly float[] ScaleLadder = { 1f, 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f };

    internal static CyclingSettingsDockEntry<float> Create(
        int order,
        string key,
        LocalizedTextSet label,
        Func<IBppConfig, float> read,
        Action<IBppConfig, float> write
    ) =>
        new(
            order,
            key,
            languageCode => label.Resolve(languageCode, L.CurrentMode),
            ScaleLadder,
            read,
            write,
            scale => Math.Abs(scale - 1f) > ComparisonTolerance,
            (scale, _) => scale.ToString("0.##", CultureInfo.InvariantCulture) + "x"
        );
}
