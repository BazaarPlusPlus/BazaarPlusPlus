using BazaarPlusPlus.Game.RandomCosmetics;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class RandomCosmeticPoolTests
{
    private static readonly string[] Candidates = ["a", "b", "c"];

    [Fact]
    public void Without_exclusions_every_candidate_is_a_member_and_the_game_pick_stands()
    {
        var pool = Create(Candidates, excluded: null);

        Assert.False(pool.OverridesGamePick);
        Assert.Equal(Candidates, pool.Members);
        Assert.Null(pool.Pick(_ => throw new InvalidOperationException("must not draw")));
    }

    [Fact]
    public void Exclusions_that_name_no_current_candidate_leave_the_game_pick()
    {
        var pool = Create(Candidates, excluded: ["gone"]);

        Assert.False(pool.OverridesGamePick);
        Assert.Null(pool.Pick(_ => 0));
    }

    [Fact]
    public void A_cosmetic_acquired_after_the_save_joins_the_pool()
    {
        var pool = Create(["a", "b", "c", "new"], excluded: ["b"]);

        Assert.Equal(["a", "c", "new"], pool.Members);
        Assert.True(pool.Contains("new"));
    }

    [Fact]
    public void A_pool_with_an_exclusion_draws_only_from_its_members_in_display_order()
    {
        var pool = Create(Candidates, excluded: ["b"]);
        var drawnFrom = 0;

        var picked = pool.Pick(count =>
        {
            drawnFrom = count;
            return 1;
        });

        Assert.True(pool.OverridesGamePick);
        Assert.Equal(2, drawnFrom);
        Assert.Equal("c", picked);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void An_index_outside_the_pool_is_an_error(int index)
    {
        var pool = Create(Candidates, excluded: ["c"]);

        Assert.Throws<InvalidOperationException>(() => pool.Pick(_ => index));
    }

    [Fact]
    public void A_click_on_a_member_leaves_it_out()
    {
        var pool = Create(Candidates, excluded: null);

        Assert.Equal(["b"], pool.Toggle("b"));
    }

    [Fact]
    public void A_click_on_a_left_out_cosmetic_brings_it_back_and_keeps_unavailable_exclusions()
    {
        var pool = Create(Candidates, excluded: ["gone", "a", "c"]);

        Assert.Equal(["c", "gone"], pool.Toggle("a"));
    }

    [Fact]
    public void The_last_member_cannot_be_left_out()
    {
        var pool = Create(Candidates, excluded: ["a", "c"]);

        Assert.Null(pool.Toggle("b"));
        Assert.True(pool.Contains("b"));
    }

    [Fact]
    public void When_every_candidate_is_left_out_the_pool_falls_back_to_all_and_a_click_starts_from_there()
    {
        var pool = Create(Candidates, excluded: ["a", "b", "c"]);

        Assert.False(pool.OverridesGamePick);
        Assert.Equal(Candidates, pool.Members);
        Assert.Equal(["b"], pool.Toggle("b"));
    }

    [Theory]
    [InlineData("default-board")]
    [InlineData("")]
    [InlineData(null)]
    public void A_click_on_a_non_candidate_changes_nothing(string? id)
    {
        var pool = Create(Candidates, excluded: null);

        Assert.Null(pool.Toggle(id));
        Assert.False(pool.Contains(id));
    }

    [Fact]
    public void Blank_and_duplicate_ids_are_ignored()
    {
        var pool = Create(["a", " ", null, "a", "b"], excluded: ["b", "b", ""]);

        Assert.Equal(["a"], pool.Members);
        Assert.Equal([], pool.Toggle("b"));
    }

    [Fact]
    public void A_category_with_no_candidates_has_no_members_and_keeps_the_game_pick()
    {
        var pool = Create([], excluded: ["a"]);

        Assert.Empty(pool.Members);
        Assert.False(pool.OverridesGamePick);
        Assert.Null(pool.Toggle("a"));
    }

    private static RandomCosmeticPool Create(
        IEnumerable<string?> candidates,
        IEnumerable<string?>? excluded
    ) => RandomCosmeticPool.Create(candidates, excluded);
}
