using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Tests;
using Xunit;

// L.Install is process-global; tests that switch the Chinese locale mode must not overlap others.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PostCombatImpact.Tests;

public sealed class CombatImpactChineseScriptTests
{
    [Fact]
    public void Taiwan_mode_converts_metric_text()
    {
        using var _ = Locale("zh-CN", BppChineseLocaleMode.Taiwan);

        Assert.Equal("觸發來源：", CombatImpactMetricFormatter.TriggerSourceLabel(chinese: true));
    }

    [Fact]
    public void Taiwan_mode_converts_attribute_labels()
    {
        using var _ = Locale("zh-CN", BppChineseLocaleMode.Taiwan);

        Assert.Equal(
            "傷害減少",
            CombatImpactAttributeLabel.Resolve(
                "DamageAmount",
                CombatImpactEventSurface.CardAttribute,
                changeValue: -5,
                chinese: true
            )
        );
    }

    [Theory]
    [InlineData(false, "对手")]
    [InlineData(true, "對手")]
    public void Fallback_entity_names_follow_the_chinese_locale_mode(bool taiwan, string expected)
    {
        using var _ = Locale(
            "zh-CN",
            taiwan ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland
        );

        Assert.Equal(expected, CombatImpactEntityName.Opponent);
    }

    [Fact]
    public void Fallback_entity_names_stay_english_outside_chinese()
    {
        using var _ = Locale("en", BppChineseLocaleMode.Taiwan);

        Assert.Equal("Opponent", CombatImpactEntityName.Opponent);
    }

    private static LocaleScope Locale(string languageCode, BppChineseLocaleMode mode)
    {
        L.Install(() => languageCode, () => mode);
        return new LocaleScope();
    }

    private sealed class LocaleScope : IDisposable
    {
        public void Dispose() => LocalizationTestBootstrap.Install();
    }
}
