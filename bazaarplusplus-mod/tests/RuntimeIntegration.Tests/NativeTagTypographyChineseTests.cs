using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Localization;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class NativeTagTypographyChineseTests
{
    [Theory]
    [InlineData("zh-CN", false, "任务")]
    [InlineData("zh-CN", true, "任務")]
    [InlineData("en", true, "Quest")]
    public void Quest_label_follows_the_chinese_locale_mode(
        string languageCode,
        bool taiwan,
        string expected
    )
    {
        try
        {
            L.Install(
                new FixedLanguage(languageCode),
                new FixedMode(taiwan ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland)
            );

            Assert.Equal(expected, NativeTagLabelText.Quest("Quest"));
        }
        finally
        {
            L.Reset();
        }
    }

    private sealed class FixedLanguage(string languageCode) : ILanguageProvider
    {
        public string CurrentLanguageCode { get; } = languageCode;
    }

    private sealed class FixedMode(BppChineseLocaleMode mode) : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode { get; } = mode;
    }
}
