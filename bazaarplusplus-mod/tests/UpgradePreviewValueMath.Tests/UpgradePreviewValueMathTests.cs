#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.Tooltips;
using Xunit;

namespace UpgradePreviewMath.Tests;

/// <summary>
/// Core's upgrade preview arithmetic, which both hosts' value projections feed their numbers
/// through: the additive projection of a runtime gain, the multiplier auras undone and applied
/// again, the cooldown clock, and the tooltip's number format.
/// </summary>
public sealed class UpgradePreviewValueMathTests
{
    [Theory]
    [InlineData(30, 25, 45, 50)] // a runtime gain of 5 carries over
    [InlineData(25, 25, 45, 45)] // no gain: the next tier's base
    [InlineData(20, 25, 45, 40)] // a runtime loss carries over too
    public void A_value_the_current_tier_carries_keeps_its_runtime_delta(
        int currentValue,
        int currentBase,
        int nextBase,
        int expected
    ) =>
        Assert.Equal(
            expected,
            UpgradePreviewValueMath.ProjectAttribute(currentValue, currentBase, nextBase)
        );

    [Fact]
    public void A_value_without_a_current_base_takes_the_next_tiers_base() =>
        Assert.Equal(12, UpgradePreviewValueMath.ProjectAttribute(7, null, 12));

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(25, 0.75f)]
    [InlineData(100, 0f)]
    [InlineData(150, 0f)]
    public void Percent_cooldown_reduction_scales_the_cooldown_and_never_turns_it_negative(
        int percent,
        float expected
    ) => Assert.Equal(expected, UpgradePreviewValueMath.CooldownMultiplier(percent), 4);

    [Fact]
    public void Identity_multipliers_leave_the_additive_projection_standing()
    {
        Assert.False(
            UpgradePreviewValueMath.TryApplyMultipliers(
                40,
                10,
                15,
                UpgradePreviewMultipliers.Identity,
                out var projected
            )
        );
        Assert.Equal(0, projected);
    }

    [Fact]
    public void A_doubling_aura_is_undone_before_the_delta_and_applied_again_after()
    {
        // 40 shown under x2 is a raw 20: base 10 plus a runtime gain of 10, so the next tier's
        // base 15 plus that gain, doubled again, is 50.
        Assert.True(
            UpgradePreviewValueMath.TryApplyMultipliers(
                40,
                10,
                15,
                new UpgradePreviewMultipliers(2f, 2f),
                out var v
            )
        );
        Assert.Equal(50, v);
    }

    [Fact]
    public void A_multiplier_only_the_next_tier_has_scales_the_projected_value()
    {
        Assert.True(
            UpgradePreviewValueMath.TryApplyMultipliers(
                10,
                10,
                15,
                new UpgradePreviewMultipliers(1f, 2f),
                out var v
            )
        );
        Assert.Equal(30, v);
    }

    [Fact]
    public void A_halved_cooldown_stays_halved_at_the_next_tier()
    {
        Assert.True(
            UpgradePreviewValueMath.TryApplyMultipliers(
                4000,
                8000,
                7000,
                new UpgradePreviewMultipliers(0.5f, 0.5f),
                out var v
            )
        );
        Assert.Equal(3500, v);
    }

    [Fact]
    public void Without_a_current_value_only_the_next_tiers_base_is_scaled()
    {
        Assert.True(
            UpgradePreviewValueMath.TryApplyMultipliers(
                null,
                null,
                9,
                new UpgradePreviewMultipliers(1f, 0.5f),
                out var v
            )
        );
        Assert.Equal(5, v); // 4.5 rounds away from zero
    }

    [Fact]
    public void A_projected_value_never_goes_below_zero()
    {
        Assert.True(
            UpgradePreviewValueMath.TryApplyMultipliers(
                0,
                10,
                5,
                new UpgradePreviewMultipliers(2f, 2f),
                out var v
            )
        );
        Assert.Equal(0, v);
    }

    [Theory]
    [InlineData(0f, 2f)]
    [InlineData(-1f, 2f)]
    [InlineData(2f, -1f)]
    public void A_multiplier_that_cannot_be_undone_leaves_the_additive_projection(
        float current,
        float projected
    ) =>
        Assert.False(
            UpgradePreviewValueMath.TryApplyMultipliers(
                40,
                10,
                15,
                new UpgradePreviewMultipliers(current, projected),
                out _
            )
        );

    [Fact]
    public void The_effective_cooldowns_are_the_two_cards_cooldowns_in_seconds()
    {
        Assert.True(
            UpgradePreviewValueMath.TryEffectiveCooldownSeconds(
                8000,
                7000,
                out var current,
                out var next
            )
        );
        Assert.Equal(8f, current);
        Assert.Equal(7f, next);
    }

    [Theory]
    [InlineData(null, 7000)]
    [InlineData(8000, null)]
    [InlineData(0, 7000)]
    [InlineData(8000, 0)]
    public void A_missing_or_empty_cooldown_has_no_clock(int? current, int? upgraded) =>
        Assert.False(
            UpgradePreviewValueMath.TryEffectiveCooldownSeconds(current, upgraded, out _, out _)
        );

    [Fact]
    public void The_tooltip_formats_like_the_game()
    {
        using var _ = new InvariantCulture();
        Assert.Equal("7.0", UpgradePreviewValueMath.CooldownText(7f));
        Assert.Equal("15", UpgradePreviewValueMath.Format(15f));
        Assert.Equal("1.5", UpgradePreviewValueMath.Format(1.5f));
        Assert.Equal("0.3", UpgradePreviewValueMath.Format(0.333f));
        Assert.Equal(1.5f, UpgradePreviewValueMath.ToRenderedUnits(1500f, inSeconds: true), 4);
        Assert.Equal(1500f, UpgradePreviewValueMath.ToRenderedUnits(1500f, inSeconds: false));
    }

    [Fact]
    public void Two_values_are_the_same_when_they_format_the_same()
    {
        using var _ = new InvariantCulture();
        Assert.True(UpgradePreviewValueMath.HaveSameFormattedValue(1.51f, 1.54f));
        Assert.False(UpgradePreviewValueMath.HaveSameFormattedValue(8f, 7f));
        // The game drops the decimal of a whole number only: 1 and 1.04 render differently.
        Assert.False(UpgradePreviewValueMath.HaveSameFormattedValue(1f, 1.04f));
    }

    [Fact]
    public void Multipliers_compose_per_side()
    {
        var both = UpgradePreviewMultipliers.Identity.Times(new UpgradePreviewMultipliers(2f, 3f));
        Assert.Equal(new UpgradePreviewMultipliers(2f, 3f), both);
        Assert.Equal(new UpgradePreviewMultipliers(1f, 3f), both.TimesCurrent(0.5f));
        Assert.Equal(new UpgradePreviewMultipliers(2f, 1.5f), both.TimesProjected(0.5f));
    }

    private sealed class InvariantCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public InvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
