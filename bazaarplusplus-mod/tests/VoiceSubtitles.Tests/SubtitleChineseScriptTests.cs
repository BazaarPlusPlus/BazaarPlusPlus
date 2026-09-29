#nullable enable

using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.Localization;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class SubtitleChineseScriptTests
{
    [Theory]
    [InlineData(false, "「这次运算结果，不太理想。」")]
    [InlineData(true, "「這次運算結果，不太理想。」")]
    public void Chinese_row_follows_the_chinese_locale_mode(bool taiwan, string expected)
    {
        try
        {
            L.Install(
                new FixedLanguage("en"),
                new FixedMode(taiwan ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland)
            );

            Assert.Equal(
                expected,
                VoiceLineDisplay.DisplayChinese(
                    " 「这次运算结果，不太理想。」 ",
                    SubtitlePosition.TopLeft
                )
            );
        }
        finally
        {
            L.Reset();
        }
    }

    [Fact]
    public void Centered_taiwan_row_converts_before_narrowing_the_terminal_punctuation()
    {
        try
        {
            L.Install(new FixedLanguage("zh-CN"), new FixedMode(BppChineseLocaleMode.Taiwan));

            Assert.Equal(
                "喜歡我的頭髮嗎?",
                VoiceLineDisplay.DisplayChinese("喜欢我的头发吗？", SubtitlePosition.TopCenter)
            );
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
