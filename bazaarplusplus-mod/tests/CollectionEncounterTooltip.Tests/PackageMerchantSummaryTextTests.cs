using System.Text;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class PackageMerchantSummaryTextTests
{
    [Fact]
    public void Uses_native_first_sentence_and_chinese_colon_for_localized_merchant()
    {
        var result = PackageMerchantSummaryText.Format(
            "橡树君",
            "出售中型或大型物品。以更高价值收购你的小型物品"
        );

        Assert.Equal("橡树君：出售中型或大型物品", result);
    }

    [Fact]
    public void Uses_ascii_colon_and_strips_later_native_rules_for_english_merchant()
    {
        var result = PackageMerchantSummaryText.Format(
            "Barkun",
            "Sells Medium and Large items. Buys your Small items at +{aura.3} Value"
        );

        Assert.Equal("Barkun: Sells Medium and Large items", result);
    }

    [Fact]
    public void Preserves_native_keyword_rich_text_in_the_first_sentence()
    {
        var result = PackageMerchantSummaryText.Format(
            "Kev's Armory",
            "<line-height=1.6em>Sells <color=#63D642>Health</color> and <color=#F5CF21>Shield</color> items. More</line-height>"
        );

        Assert.Equal(
            "Kev's Armory: <line-height=1.6em>Sells <color=#63D642>Health</color> and <color=#F5CF21>Shield</color> items</line-height>",
            result
        );
    }

    [Fact]
    public void Does_not_treat_decimal_inside_tmp_tag_as_a_sentence_terminator()
    {
        var result = PackageMerchantSummaryText.Format(
            "Herma",
            "<line-height=1.6em>Sells <color=#63D642>Heal</color> and Regen items</line-height>"
        );

        Assert.Equal(
            "Herma: <line-height=1.6em>Sells <color=#63D642>Heal</color> and Regen items</line-height>",
            result
        );
    }

    [Fact]
    public void Passive_markup_uses_compact_spacing_and_preserves_keyword_markup()
    {
        var result = PackageMerchantSummaryText.WrapForPassiveBlock(
            "Herma: <line-height=1.6em>Sells <color=#63D642>Heal</color> items</line-height>"
        );

        Assert.Equal(
            "<size=80%><line-height=1.1em>\n</line-height><line-height=1.6em>Herma: Sells <color=#63D642>Heal</color> items</line-height></size>",
            result
        );
    }

    [Fact]
    public void Replaces_native_trailing_newline_with_the_single_controlled_break()
    {
        var passiveText = new StringBuilder("When sold, gain an item\r\n");

        PackageMerchantSummaryText.AppendToPassiveBlock(passiveText, "Pol: Sells Large items");

        Assert.Equal(
            "When sold, gain an item<size=80%><line-height=1.1em>\n</line-height><line-height=1.6em>Pol: Sells Large items</line-height></size>",
            passiveText.ToString()
        );
    }

    [Fact]
    public void Normalizes_only_scaled_inline_sprite_wrappers_to_one_em()
    {
        var result = PackageMerchantSummaryText.WrapForPassiveBlock(
            "Kev's Armory: <line-height=1.6em>Sells "
                + "<size=145%><voffset=-4><sprite name=Health><font=Numbers><color=green></font></size></voffset>Health</color> "
                + "and <size=145%><voffset=-4><font=Numbers><color=yellow>Shield</color></font></size></voffset> items</line-height>"
        );

        Assert.Contains("<size=100%><voffset=-4><sprite name=Health>", result);
        Assert.Contains(
            "<size=145%><voffset=-4><font=Numbers><color=yellow>Shield</color>",
            result
        );
    }

    [Theory]
    [InlineData(null, "Sells items")]
    [InlineData("Barkun", null)]
    [InlineData(" ", "Sells items")]
    [InlineData("Barkun", " ")]
    public void Returns_empty_when_native_title_or_description_is_missing(
        string? title,
        string? description
    )
    {
        Assert.Empty(PackageMerchantSummaryText.Format(title, description));
    }

    // The merchant summary cache: key scope is the static-data manager reference plus the game
    // language, and only static-data outcomes (PackageMerchantSummary.IsCacheable) are kept.
    [Fact]
    public void Summary_cache_reuses_a_resolution_only_within_one_manager_and_language()
    {
        var cache = new StaticDataScopedCache<Guid, string>();
        var package = Guid.NewGuid();
        var managerA = new object();
        var managerB = new object();
        var resolver = new CountingResolver();

        Assert.Equal("1", Resolve(cache, managerA, "en", package, resolver));
        Assert.Equal("1", Resolve(cache, managerA, "en", package, resolver));

        Assert.Equal("2", Resolve(cache, managerB, "en", package, resolver));
        Assert.Equal("3", Resolve(cache, managerA, "en", package, resolver));

        Assert.Equal("4", Resolve(cache, managerA, "zh-CN", package, resolver));
        Assert.Equal("5", Resolve(cache, managerA, "en", package, resolver));
        Assert.Equal("5", Resolve(cache, managerA, "en", package, resolver));
    }

    [Theory]
    [InlineData(nameof(PackageMerchantSummaryFailureReason.None), true)]
    [InlineData(nameof(PackageMerchantSummaryFailureReason.MerchantReferenceUnavailable), true)]
    [InlineData(nameof(PackageMerchantSummaryFailureReason.MerchantTemplateUnavailable), true)]
    [InlineData(nameof(PackageMerchantSummaryFailureReason.NativeTooltipUnavailable), false)]
    [InlineData(nameof(PackageMerchantSummaryFailureReason.EmptyText), false)]
    public void Summary_cache_keeps_static_data_outcomes_and_recomputes_transient_failures(
        string outcomeName,
        bool cached
    )
    {
        var outcome = Enum.Parse<PackageMerchantSummaryFailureReason>(outcomeName);
        var cache = new StaticDataScopedCache<Guid, PackageMerchantSummaryFailureReason>();
        var package = Guid.NewGuid();
        var manager = new object();
        var calls = new CountingResolver();

        for (var render = 0; render < 2; render++)
            cache.GetOrAdd(
                manager,
                "en",
                package,
                (calls, outcome),
                static state =>
                {
                    state.calls.Next();
                    return state.outcome;
                },
                static resolved => PackageMerchantSummary.IsCacheable(resolved)
            );

        Assert.Equal(cached ? 1 : 2, calls.Count);
    }

    private static string Resolve(
        StaticDataScopedCache<Guid, string> cache,
        object manager,
        string language,
        Guid package,
        CountingResolver resolver
    ) =>
        cache.GetOrAdd(
            manager,
            language,
            package,
            resolver,
            static state => state.Next(),
            static _ => true
        );

    private sealed class CountingResolver
    {
        internal int Count { get; private set; }

        internal string Next() =>
            (++Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
