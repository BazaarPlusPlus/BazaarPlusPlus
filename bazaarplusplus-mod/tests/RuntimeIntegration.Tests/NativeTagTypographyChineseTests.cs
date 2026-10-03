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
                () => languageCode,
                () => taiwan ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland
            );

            Assert.Equal(expected, NativeTagLabelText.Quest("Quest"));
        }
        finally
        {
            L.Reset();
        }
    }
}
