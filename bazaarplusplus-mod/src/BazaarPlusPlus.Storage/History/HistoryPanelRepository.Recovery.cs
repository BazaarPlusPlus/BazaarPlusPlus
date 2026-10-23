#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

public sealed record HiddenGhost(
    HistoryCursor Cursor,
    string Account,
    string Uploader,
    string RemoteId,
    string Bundle,
    long DeletedAtMs
);

public sealed partial class HistoryPanelRepository
{
    public HistoryCursorPage<HiddenGhost> ListHiddenGhosts(string account, HistoryCursor? cursor) =>
        ReadCursorPage(
            HistoryPageQuery.HiddenGhosts(account),
            "battle_id, recorded_at_ms, local_player_account_id, uploader_account_id, remote_battle_id, bundle_id, deleted_at_ms",
            new(cursor),
            reader => new HiddenGhost(
                new(reader.GetInt64(1), reader.GetString(0)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6)
            )
        );

    public bool RestoreHiddenGhost(HiddenGhost ghost)
    {
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE battles SET deleted_at_ms = NULL
            WHERE source = 'GHOST' AND battle_id = $battle AND local_player_account_id = $account
                AND uploader_account_id = $uploader AND remote_battle_id = $remote AND bundle_id = $bundle
                AND deleted_at_ms = $deleted AND ghost_replay_state = 'local_ready';
            """;
        command.Parameters.AddWithValue("$battle", ghost.Cursor.Id);
        command.Parameters.AddWithValue("$account", ghost.Account);
        command.Parameters.AddWithValue("$uploader", ghost.Uploader);
        command.Parameters.AddWithValue("$remote", ghost.RemoteId);
        command.Parameters.AddWithValue("$bundle", ghost.Bundle);
        command.Parameters.AddWithValue("$deleted", ghost.DeletedAtMs);
        return command.ExecuteNonQuery() == 1;
    }
}
