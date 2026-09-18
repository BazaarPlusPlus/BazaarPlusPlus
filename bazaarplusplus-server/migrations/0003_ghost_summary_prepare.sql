-- Apply before the column-based Worker. No historical rows are copied here.
CREATE TABLE ghost_battle_summaries (
  uploader_account_id TEXT NOT NULL,
  battle_id TEXT NOT NULL,
  bundle_id TEXT NOT NULL REFERENCES bundles(bundle_id) ON DELETE CASCADE,
  opponent_account_id TEXT NOT NULL,
  recorded_at_ms INTEGER NOT NULL,
  is_final_battle INTEGER NOT NULL DEFAULT 0 CHECK (is_final_battle IN (0, 1)),
  day INTEGER NOT NULL,
  hour INTEGER NOT NULL,
  result TEXT NOT NULL,
  winner_combatant_id TEXT,
  player_display_name TEXT NOT NULL,
  player_hero_name TEXT,
  opponent_hero_name TEXT,
  player_rank TEXT,
  player_rating INTEGER,
  PRIMARY KEY (uploader_account_id, battle_id)
) WITHOUT ROWID;

CREATE INDEX idx_ghost_summaries_query
  ON ghost_battle_summaries(opponent_account_id, recorded_at_ms DESC, battle_id DESC);
CREATE INDEX idx_ghost_summaries_bundle ON ghost_battle_summaries(bundle_id);

CREATE TABLE ghost_projection_migration (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  phase TEXT NOT NULL CHECK (phase IN ('copying', 'verifying', 'verified', 'bridge', 'retired')),
  cursor_uploader TEXT NOT NULL DEFAULT '',
  cursor_battle TEXT NOT NULL DEFAULT '',
  legacy_duplicates INTEGER NOT NULL DEFAULT 0,
  retire_authorized INTEGER NOT NULL DEFAULT 0 CHECK (retire_authorized IN (0, 1))
);
INSERT INTO ghost_projection_migration (id, phase) VALUES (1, 'copying');

CREATE TRIGGER ghost_legacy_to_summary AFTER INSERT ON ghost_battles
BEGIN
  INSERT INTO ghost_battle_summaries (uploader_account_id, battle_id, bundle_id, opponent_account_id, recorded_at_ms, is_final_battle, day, hour, result, winner_combatant_id, player_display_name, player_hero_name, opponent_hero_name, player_rank, player_rating)
  VALUES (
    NEW.uploader_account_id,
    NEW.battle_id,
    NEW.bundle_id,
    NEW.opponent_account_id,
    NEW.recorded_at_ms,
    NEW.is_final_battle,
    json_extract(NEW.projection_json, '$.day'),
    json_extract(NEW.projection_json, '$.hour'),
    json_extract(NEW.projection_json, '$.result'),
    json_extract(NEW.projection_json, '$.winner_combatant_id'),
    json_extract(NEW.projection_json, '$.player.display_name'),
    json_extract(NEW.projection_json, '$.player.hero_name'),
    json_extract(NEW.projection_json, '$.opponent.hero_name'),
    json_extract(NEW.projection_json, '$.player.rank'),
    json_extract(NEW.projection_json, '$.player.rating')
  ) ON CONFLICT(uploader_account_id, battle_id) DO NOTHING;
END;

-- D1 meta.changes includes trigger writes. Preserve old-Worker anomaly counts
-- independently until all writers use INSERT RETURNING.
CREATE TRIGGER ghost_legacy_duplicate BEFORE INSERT ON ghost_battles
WHEN EXISTS (
  SELECT 1 FROM ghost_battles
  WHERE uploader_account_id = NEW.uploader_account_id AND battle_id = NEW.battle_id
    AND bundle_id <> NEW.bundle_id
)
BEGIN
  UPDATE ghost_projection_migration SET legacy_duplicates = legacy_duplicates + 1 WHERE id = 1;
END;

-- Only a legacy row may create a summary until verification enables both writers.
CREATE TRIGGER ghost_summary_copy_guard BEFORE INSERT ON ghost_battle_summaries
WHEN NOT EXISTS (
  SELECT 1 FROM ghost_battles
  WHERE uploader_account_id = NEW.uploader_account_id AND battle_id = NEW.battle_id
    AND bundle_id = NEW.bundle_id
)
BEGIN SELECT RAISE(ABORT, 'Ghost summary backfill is not ready'); END;

CREATE TRIGGER ghost_legacy_immutable BEFORE UPDATE ON ghost_battles
BEGIN SELECT RAISE(ABORT, 'Ghost projections are immutable'); END;
