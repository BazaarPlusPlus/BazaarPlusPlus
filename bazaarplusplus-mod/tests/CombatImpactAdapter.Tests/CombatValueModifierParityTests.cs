using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.GameInterop.CombatSimulation;
using Xunit;

namespace CombatImpactAdapter.Tests;

/// <summary>
/// Arithmetic parity (ADR-0019): Core's <see cref="CombatValueModifier.GetModifiedValue"/> restates
/// the game's <c>TValueModifier.GetModifiedValue</c> over a fixed operand, because Core cannot call
/// the game. Every mode the Managed snapshot defines, with and without rounding, must give the same
/// float bits as the game for every value pair here: fractions between zero and one, rounding
/// midpoints on both signs, and a zero divisor.
/// </summary>
public sealed class CombatValueModifierParityTests
{
    private static readonly float[] Originals =
    [
        0f,
        1f,
        -1f,
        0.3f,
        0.5f,
        0.999f,
        1.5f,
        2.5f,
        -0.5f,
        -1.5f,
        -2.5f,
        3f,
        5f,
        -5f,
        7f,
        100.5f,
    ];

    private static readonly float[] Operands =
    [
        0f,
        1f,
        -1f,
        0.1f,
        0.25f,
        0.5f,
        -0.5f,
        1.5f,
        2f,
        3f,
        4f,
        -2f,
    ];

    [Fact]
    public void Every_game_mode_matches_the_game_arithmetic_over_fixed_operands()
    {
        var mismatches = new List<string>();
        foreach (var mode in Enum.GetValues<EValueModifierMode>())
        {
            Assert.True(
                DesktopCombatFacets.ValueModifierMode.TryToFacet(mode, out var facet),
                $"EValueModifierMode.{mode} has no Core facet, so Core cannot restate it."
            );
            foreach (var shouldRound in new[] { true, false })
            {
                foreach (var original in Originals)
                {
                    foreach (var operand in Operands)
                    {
                        var game = Game(mode, shouldRound, original, operand);
                        var core = Core(facet, shouldRound, original, operand);
                        if (
                            BitConverter.SingleToInt32Bits(game)
                            != BitConverter.SingleToInt32Bits(core)
                        )
                        {
                            mismatches.Add(
                                $"{mode} round={shouldRound} {original} by {operand}: game={game} core={core}"
                            );
                        }
                    }
                }
            }
        }
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    // The cases the grid must reach, with the game's answer written out so a change in the game's
    // rounding shows up as a named case rather than only as a parity count.
    [Theory]
    [InlineData(EValueModifierMode.Multiply, true, 1f, 0.5f, 1f)]
    [InlineData(EValueModifierMode.Multiply, false, 1f, 0.5f, 0.5f)]
    [InlineData(EValueModifierMode.Multiply, true, 3f, 0.5f, 2f)]
    [InlineData(EValueModifierMode.Multiply, true, 5f, 0.5f, 3f)]
    [InlineData(EValueModifierMode.Multiply, true, -5f, 0.5f, -3f)]
    [InlineData(EValueModifierMode.Multiply, true, -1f, 0.5f, -1f)]
    [InlineData(EValueModifierMode.Divide, true, 1f, 4f, 1f)]
    [InlineData(EValueModifierMode.Divide, false, 1f, 4f, 0.25f)]
    [InlineData(EValueModifierMode.Divide, true, 7f, 0f, 7f)]
    [InlineData(EValueModifierMode.Divide, false, 7f, 0f, 7f)]
    [InlineData(EValueModifierMode.Add, true, 2f, 0.5f, 2.5f)]
    [InlineData(EValueModifierMode.Subtract, true, 1.5f, 0.25f, 1.25f)]
    public void Edge_cases_give_the_game_answer(
        EValueModifierMode mode,
        bool shouldRound,
        float original,
        float operand,
        float expected
    )
    {
        Assert.True(DesktopCombatFacets.ValueModifierMode.TryToFacet(mode, out var facet));

        Assert.Equal(expected, Game(mode, shouldRound, original, operand));
        Assert.Equal(expected, Core(facet, shouldRound, original, operand));
    }

    private static float Game(
        EValueModifierMode mode,
        bool shouldRound,
        float original,
        float operand
    ) =>
        new TValueModifier
        {
            ModifyMode = mode,
            Value = new TFixedValue { Value = operand },
            ShouldRound = shouldRound,
        }.GetModifiedValue(original, default);

    private static float Core(
        CombatValueModifierMode mode,
        bool shouldRound,
        float original,
        float operand
    ) =>
        new CombatValueModifier
        {
            ModifyMode = mode,
            Value = new CombatFixedValue { Value = operand },
            ShouldRound = shouldRound,
        }.GetModifiedValue(original, operand);
}
