-- Guard against applying all pending migrations to a populated database.
-- The runner verifies every retained field in bounded pages before this stage.
CREATE TABLE ghost_migration_gate (ok INTEGER NOT NULL CHECK (ok = 1));
INSERT INTO ghost_migration_gate VALUES (CASE WHEN
  (SELECT phase FROM ghost_projection_migration WHERE id = 1) = 'verified'
  OR (EXISTS (SELECT 1 FROM ghost_projection_migration WHERE id = 1)
    AND NOT EXISTS (SELECT 1 FROM ghost_battles)) THEN 1 ELSE 0 END);
DROP TABLE ghost_migration_gate;

DROP TRIGGER ghost_summary_copy_guard;
CREATE TRIGGER ghost_summary_to_legacy AFTER INSERT ON ghost_battle_summaries
BEGIN
  INSERT INTO ghost_battles (
    uploader_account_id, battle_id, bundle_id, opponent_account_id,
    recorded_at_ms, is_final_battle, projection_json
  ) VALUES (
    NEW.uploader_account_id, NEW.battle_id, NEW.bundle_id, NEW.opponent_account_id,
    NEW.recorded_at_ms, NEW.is_final_battle,
    json_object(
      'battle_id', NEW.battle_id, 'recorded_at_ms', NEW.recorded_at_ms,
      'is_final_battle', json(CASE NEW.is_final_battle WHEN 1 THEN 'true' ELSE 'false' END),
      'day', NEW.day, 'hour', NEW.hour, 'result', NEW.result,
      'winner_combatant_id', NEW.winner_combatant_id,
      'player', json_object('account_id', NEW.uploader_account_id,
        'display_name', NEW.player_display_name, 'hero_name', NEW.player_hero_name,
        'rank', NEW.player_rank, 'rating', NEW.player_rating),
      'opponent', json_object('account_id', NEW.opponent_account_id,
        'hero_name', NEW.opponent_hero_name)
    )
  ) ON CONFLICT(uploader_account_id, battle_id) DO NOTHING;
END;
UPDATE ghost_projection_migration SET phase = 'bridge' WHERE id = 1;
