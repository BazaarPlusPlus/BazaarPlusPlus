#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using Xunit;

// A Ghost is marked when its challenger fought a build the player favorited: the player's hero,
// day, level, prestige and victories on both sides, and the Ghost recorded after the favorite.
public sealed class FavoriteBuildsTests
{
    private static readonly DateTimeOffset Favorited = DateTimeOffset.Parse("2026-11-13T12:00:00Z");

    [Fact]
    public void A_Ghost_that_fought_a_favorite_build_matches()
    {
        var builds = FavoriteBuilds.From([Battle(HistoryBattleSource.Local)]);

        Assert.True(builds.Matches(Battle(HistoryBattleSource.Ghost, at: Favorited.AddHours(3))));
        Assert.True(
            builds.Matches(Battle(HistoryBattleSource.Ghost, hero: " dooley ", at: Favorited)),
            "The hero compares trimmed and case-blind."
        );
    }

    [Theory]
    [InlineData("Vanessa", 11, 12, 5, 6)]
    [InlineData("Dooley", 12, 12, 5, 6)]
    [InlineData("Dooley", 11, 13, 5, 6)]
    [InlineData("Dooley", 11, 12, 4, 6)]
    [InlineData("Dooley", 11, 12, 5, 7)]
    public void A_Ghost_with_any_other_player_side_does_not_match(
        string hero,
        int day,
        int level,
        int prestige,
        int victories
    )
    {
        var builds = FavoriteBuilds.From([Battle(HistoryBattleSource.Local)]);

        Assert.False(
            builds.Matches(
                Battle(
                    HistoryBattleSource.Ghost,
                    hero,
                    day,
                    level,
                    prestige,
                    victories,
                    Favorited.AddHours(1)
                )
            )
        );
    }

    [Fact]
    public void A_Ghost_recorded_before_the_favorite_matches_only_within_clock_skew()
    {
        var builds = FavoriteBuilds.From([Battle(HistoryBattleSource.Local)]);

        Assert.True(
            builds.Matches(
                Battle(HistoryBattleSource.Ghost, at: Favorited - FavoriteBuilds.ClockSkew)
            )
        );
        Assert.False(
            builds.Matches(
                Battle(
                    HistoryBattleSource.Ghost,
                    at: Favorited - FavoriteBuilds.ClockSkew - TimeSpan.FromSeconds(1)
                )
            )
        );
    }

    [Fact]
    public void Only_complete_favorites_of_own_battles_from_day_five_count()
    {
        var early = Battle(HistoryBattleSource.Local, day: FavoriteBuilds.MinimumDay - 1);
        var ghostOrigin = Battle(HistoryBattleSource.Ghost);
        var unknownVictories = Battle(HistoryBattleSource.Local, victories: null);
        var noHero = Battle(HistoryBattleSource.Local, hero: " ");

        var builds = FavoriteBuilds.From([early, ghostOrigin, unknownVictories, noHero]);

        Assert.Same(FavoriteBuilds.None, builds);
        Assert.False(
            builds.Matches(Battle(HistoryBattleSource.Ghost, day: FavoriteBuilds.MinimumDay - 1))
        );
        Assert.Equal(
            1,
            FavoriteBuilds
                .From([Battle(HistoryBattleSource.Local, day: FavoriteBuilds.MinimumDay)])
                .Count
        );
    }

    [Fact]
    public void A_Ghost_without_the_players_progress_or_a_local_battle_never_matches()
    {
        var builds = FavoriteBuilds.From([Battle(HistoryBattleSource.Local)]);

        // A Ghost stored before the server returned the local player's progress.
        Assert.False(
            builds.Matches(
                Battle(HistoryBattleSource.Ghost, level: null, prestige: null, victories: null)
            )
        );
        Assert.False(builds.Matches(Battle(HistoryBattleSource.Local)));
    }

    [Fact]
    public void A_marked_Ghost_row_leads_its_meta_with_the_mark()
    {
        var ghost = Battle(HistoryBattleSource.Ghost);

        var marked = HistoryPanelRows.Ghost(ghost, hero => hero ?? string.Empty, Favorited, true);
        var plain = HistoryPanelRows.Ghost(ghost, hero => hero ?? string.Empty, Favorited);

        Assert.Equal($"{HistoryPanelText.FavoriteBuildMark()} · {plain.Meta}", marked.Meta);
        Assert.Equal(plain with { Meta = marked.Meta }, marked);
    }

    // The local player's side is the record's Player side, for a Ghost as for an own battle.
    private static HistoryBattleRecord Battle(
        HistoryBattleSource source,
        string? hero = "Dooley",
        int? day = 11,
        int? level = 12,
        int? prestige = 5,
        int? victories = 6,
        DateTimeOffset? at = null
    ) =>
        new(
            "battle",
            source == HistoryBattleSource.Local ? "run" : string.Empty,
            at ?? Favorited,
            day,
            1,
            null,
            hero,
            null,
            null,
            level,
            prestige,
            victories,
            "Challenger",
            "Vanessa",
            null,
            null,
            null,
            null,
            null,
            null,
            "PVPCombat",
            null,
            null,
            null,
            HistoryBattleSnapshotCounts.Unknown,
            false,
            source,
            ReplayAvailability.Unavailable
        );
}
