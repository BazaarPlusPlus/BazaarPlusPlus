//! A Run's Final Board: the items on the board of its last local battle whose
//! board the mod captured. The mod writes each battle's board to
//! `battle_snapshots.player_hand_json` in the shape of the mod Core's
//! `PvpBattleCardSetDocument`.

/// One row per item on a Final Board: `run_id`, `template_id`, `name` (the
/// item's English internal name). A battle whose board read failed has status
/// `Missing` and no items, so the board falls back to the battle before it.
/// Malformed JSON reads as no board rather than failing the query. The last
/// captured battle is looked up once per Run, newest first along
/// `idx_battles_run_id_recorded`, so the cost grows with Runs, not battles.
pub(crate) const FINAL_BOARD_ITEMS: &str = "
    select
      final.run_id as run_id,
      json_extract(item.value, '$.template_id') as template_id,
      json_extract(item.value, '$.name') as name
    from (
      select
        r.run_id as run_id,
        (
          select b.battle_id
          from battles b
          join battle_snapshots s on s.battle_id = b.battle_id
          where b.run_id = r.run_id
            and b.source = 'LOCAL'
            and case when json_valid(s.player_hand_json)
              then json_extract(s.player_hand_json, '$.status') end = 'Captured'
          order by b.recorded_at_ms desc, b.battle_id desc
          limit 1
        ) as battle_id
      from runs r
    ) as final
    join battle_snapshots s on s.battle_id = final.battle_id,
    json_each(
      case when json_valid(s.player_hand_json) then s.player_hand_json else '{}' end,
      '$.items'
    ) as item
    where json_extract(item.value, '$.type') = 'Item'
      and json_extract(item.value, '$.template_id') is not null
";

#[cfg(test)]
mod tests {
    // Isolated on purpose: the snapshot JSON is a contract with the mod, and a
    // rename there fails silently here. Failure modes:
    // - The mod renames `items`, `template_id`, `type`, `name` or `status`, or the
    //   values `Item` or `Captured`: every Final Board reads as empty.
    // - A socket effect (`SocketEffect`) on the board counts as an item.
    // The mod pins the shape in its own golden, read here as written.
    use super::FINAL_BOARD_ITEMS;
    use crate::history::mod_schema::create_mod_schema;

    const MOD_SNAPSHOT_GOLDEN: &str = include_str!(
        "../../../../bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/battle-snapshots.golden.txt"
    );

    #[test]
    fn final_board_reads_the_mod_snapshot_shape() {
        let column = |name: &str| {
            MOD_SNAPSHOT_GOLDEN
                .lines()
                .find_map(|line| line.strip_prefix(&format!("{name} ")))
                .unwrap()
                .to_string()
        };
        let conn = rusqlite::Connection::open_in_memory().unwrap();
        create_mod_schema(&conn);
        conn.execute_batch(
            "insert into runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode)
             values ('run-1', 1, 1, 'completed', 'Pygmalien', 'Ranked');
             insert into battles (battle_id, source, run_id, recorded_at_ms, combat_kind, local_payload_state)
             values ('battle-1', 'LOCAL', 'run-1', 1, 'PVP', 'missing');",
        )
        .unwrap();
        conn.execute(
            "insert into battle_snapshots (battle_id, player_hand_json, player_skills_json, opponent_hand_json, opponent_skills_json)
             values ('battle-1', ?1, ?2, ?3, ?4)",
            rusqlite::params![
                column("player_hand_json"),
                column("player_skills_json"),
                column("opponent_hand_json"),
                column("opponent_skills_json"),
            ],
        )
        .unwrap();

        let mut stmt = conn
            .prepare(&format!(
                "select run_id, template_id, name from ({FINAL_BOARD_ITEMS})"
            ))
            .unwrap();
        let items = stmt
            .query_map([], |row| {
                Ok((
                    row.get::<_, String>(0)?,
                    row.get::<_, String>(1)?,
                    row.get::<_, Option<String>>(2)?,
                ))
            })
            .unwrap()
            .collect::<Result<Vec<_>, _>>()
            .unwrap();
        assert_eq!(
            items,
            [(
                "run-1".to_string(),
                "6a0c3f5e-0000-4000-8000-000000000001".to_string(),
                Some("Pigomorph".to_string())
            )]
        );
    }
}
