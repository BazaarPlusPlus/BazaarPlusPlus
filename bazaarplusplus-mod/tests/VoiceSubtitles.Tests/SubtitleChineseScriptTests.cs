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
            Install(taiwan);

            Assert.Equal(
                expected,
                VoiceLineDisplay.DisplayChinese(
                    " 「这次运算结果，不太理想。」 ",
                    SubtitlePosition.TopLeft,
                    _ => true,
                    out var keptSimplified
                )
            );
            Assert.False(keptSimplified);
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
            Install(taiwan: true);

            Assert.Equal(
                "喜歡我的頭髮嗎?",
                VoiceLineDisplay.DisplayChinese(
                    "喜欢我的头发吗？",
                    SubtitlePosition.TopCenter,
                    _ => true,
                    out _
                )
            );
        }
        finally
        {
            L.Reset();
        }
    }

    [Fact]
    public void Taiwan_row_keeps_simplified_text_when_the_font_lacks_a_converted_glyph()
    {
        try
        {
            Install(taiwan: true);
            var probed = new List<char>();

            var text = VoiceLineDisplay.DisplayChinese(
                "喜欢我的头发吗？",
                SubtitlePosition.TopLeft,
                character =>
                {
                    probed.Add(character);
                    return character != '髮';
                },
                out var keptSimplified
            );

            Assert.Equal("喜欢我的头发吗？", text);
            Assert.True(keptSimplified);
            // Only glyphs the conversion introduced are probed; the Simplified line already rendered.
            Assert.DoesNotContain('我', probed);
        }
        finally
        {
            L.Reset();
        }
    }

    [Fact]
    public void Mainland_row_never_probes_the_font()
    {
        try
        {
            Install(taiwan: false);

            var text = VoiceLineDisplay.DisplayChinese(
                "喜欢我的头发吗？",
                SubtitlePosition.TopLeft,
                _ => throw new InvalidOperationException("Mainland text must not be probed."),
                out var keptSimplified
            );

            Assert.Equal("喜欢我的头发吗？", text);
            Assert.False(keptSimplified);
        }
        finally
        {
            L.Reset();
        }
    }

    private static void Install(bool taiwan) =>
        L.Install(
            () => "zh-CN",
            () => taiwan ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland
        );
}
