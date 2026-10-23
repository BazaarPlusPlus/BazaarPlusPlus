#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

// A favorite is a snapshot of its battle: it outlives the run, keeps its own replay state and
// note, and lists newest-favorited first without opening its board documents.
internal static class HistoryFavoritesTests
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "favorites.sqlite3");
        using (var seed = new SqliteConnection($"Data Source={path}"))
        {
            seed.Open();
            RunLogSchema.EnsureInitialized(seed);
            using var command = seed.CreateCommand();
            command.CommandText = """
                INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode)
                VALUES ('fav-run', 1767225600000, 1767225600000, 'completed', 'Vanessa', 'Ranked');
                INSERT INTO battles (
                    battle_id, source, run_id, recorded_at_ms, day, combat_kind,
                    local_payload_state, opponent_name, opponent_hero
                ) VALUES (
                    'fav-local', 'LOCAL', 'fav-run', 1767225600000, 4, 'PVPCombat',
                    'ready', 'Rival', 'Pygmalien'
                );
                INSERT INTO battle_snapshots (
                    battle_id, player_hand_json, player_skills_json, opponent_hand_json,
                    opponent_skills_json
                ) VALUES (
                    'fav-local',
                    '{"items":[{"template_id":"local-card","type":"Item","size":"Small"}]}',
                    '{"items":[]}', '{"items":[]}', '{"items":[]}'
                );
                INSERT INTO battles (
                    battle_id, source, remote_battle_id, uploader_account_id,
                    local_player_account_id, recorded_at_ms, day, combat_kind
                ) VALUES (
                    'fav-ghost', 'GHOST', 'remote-fav', 'account-uploader', 'account-local',
                    1767225700000, 9, 'PVPCombat'
                );
                """;
            command.ExecuteNonQuery();
        }

        var repository = new HistoryPanelRepository(path);
        var earlier = DateTimeOffset.FromUnixTimeMilliseconds(1767300000000);
        var later = earlier.AddMinutes(5);
        Check(
            repository.AddFavorite("fav-local", replayCopySaved: true, null, earlier),
            "A local battle must become a favorite."
        );
        Check(
            !repository.AddFavorite("fav-local", replayCopySaved: false, null, later),
            "Favoriting twice must keep the first favorite."
        );
        Check(
            !repository.AddFavorite("missing", replayCopySaved: false, null, later),
            "A battle that is no longer in History cannot be favorited."
        );
        var ghostSnapshots = new PvpBattleSnapshots
        {
            PlayerHand = new PvpBattleCardSetCapture
            {
                Items = [new PvpBattleCardSnapshot { TemplateId = "ghost-card" }],
            },
        };
        Check(
            repository.AddFavorite(
                "fav-ghost",
                replayCopySaved: false,
                PvpBattleSnapshotsCodec.ToDocuments(ghostSnapshots),
                later
            ),
            "A Ghost battle must become a favorite with the boards from its payload."
        );

        var page = repository.ListFavorites(new());
        Check(
            page.TotalCount == 2
                && page.Rows.Select(row => row.BattleId).SequenceEqual(["fav-ghost", "fav-local"]),
            "Favorites must list newest-favorited first."
        );
        var local = page.Rows[1];
        Check(
            local.Battle.Source == HistoryBattleSource.Local
                && local.Battle.Replay == ReplayAvailability.Saved
                && local.Battle.OpponentName == "Rival"
                && local.FavoritedAtUtc == earlier
                && local.Note == string.Empty,
            "A local favorite must keep its battle facts and its saved replay copy."
        );
        var ghost = page.Rows[0];
        Check(
            ghost.Battle.Source == HistoryBattleSource.Ghost
                && ghost.Battle.Replay == ReplayAvailability.Unavailable,
            "A favorite saved without a copy must not offer a replay."
        );
        Check(
            Snapshots(repository, "fav-local")?.PlayerHand.Items.Single().TemplateId
                == "local-card",
            "A local favorite must copy its boards from battle_snapshots."
        );
        Check(
            Snapshots(repository, "fav-ghost")?.PlayerHand.Items.Single().TemplateId
                == "ghost-card",
            "A Ghost favorite must keep the boards it was given."
        );
        Check(
            repository.ListFavoriteBattleIds().SetEquals(["fav-local", "fav-ghost"]),
            "The orphan sweep must see every favorite."
        );

        var note = HistoryFavoriteService.NormalizeNote(
            "\r\n  Lifesteal carried this.\r\nSecond line  \n" + new string('x', 600)
        );
        Check(
            note.Length == RunLogSchema.FavoriteNoteMaxLength
                && note.StartsWith(
                    "Lifesteal carried this.\nSecond line",
                    StringComparison.Ordinal
                ),
            "Notes must be trimmed, use \\n line breaks and stop at the schema limit."
        );
        Check(
            repository.UpdateFavoriteNote("fav-local", "Lifesteal carried this.", later),
            "A favorite's note must be editable."
        );
        Check(
            !repository.UpdateFavoriteNote("missing", "note", later),
            "Editing a removed favorite must report it."
        );
        Check(
            Throws(() => repository.UpdateFavoriteNote("fav-local", new string('x', 501), later)),
            "The repository must refuse a note over the schema limit."
        );

        repository.DeleteRun("fav-run");
        var survivor = repository
            .ListFavorites(new())
            .Rows.Single(row => row.BattleId == "fav-local");
        Check(
            survivor.Note == "Lifesteal carried this."
                && survivor.Battle.OpponentName == "Rival"
                && repository.LoadFavoriteSnapshots("fav-local") != null,
            "Deleting the run must keep the favorite, its note and its boards."
        );

        Check(repository.DeleteFavorite("fav-local"), "A favorite must be removable.");
        Check(
            !repository.IsFavorite("fav-local") && repository.IsFavorite("fav-ghost"),
            "Removing one favorite must leave the others."
        );
        Check(
            !repository.DeleteFavorite("fav-local"),
            "Removing twice must report nothing removed."
        );
    }

    private static PvpBattleSnapshots? Snapshots(HistoryPanelRepository repository, string id) =>
        repository.LoadFavoriteSnapshots(id) is { } documents
            ? PvpBattleSnapshotsCodec.FromDocuments(documents)
            : null;

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
