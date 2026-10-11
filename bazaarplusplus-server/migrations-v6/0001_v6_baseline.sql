-- V6 D1 baseline: the schema ../migrations/0001-0006 leave, with Bundle and Run
-- format version 6 and without the BazaarDB claim/settle tables (V6 delivers
-- through the GET /bazaardb/bundles cursor feed). It creates a new D1;
-- ../migrations/ stays with the V5 D1 until the V5 Worker is retired.
PRAGMA foreign_keys = ON;

CREATE TABLE bundles (
  bundle_id               TEXT PRIMARY KEY,
  run_id                  TEXT NOT NULL UNIQUE,
  uploader_account_id     TEXT NOT NULL,
  object_key              TEXT NOT NULL,
  bundle_sha256           TEXT NOT NULL CHECK (
    length(bundle_sha256) = 64
    AND bundle_sha256 NOT GLOB '*[^0-9a-f]*'
  ),
  bundle_version          INTEGER NOT NULL CHECK (bundle_version = 6),
  manifest_bytes          INTEGER NOT NULL CHECK (manifest_bytes BETWEEN 1 AND 2097152),
  object_bytes            INTEGER NOT NULL CHECK (object_bytes BETWEEN 1 AND 8388607),
  client_created_at_ms    INTEGER NOT NULL,
  stored_at_ms            INTEGER NOT NULL,
  available_at_ms         INTEGER NOT NULL,

  run_format_version      INTEGER NOT NULL CHECK (run_format_version = 6),
  run_bytes               INTEGER NOT NULL CHECK (run_bytes BETWEEN 1 AND 2097151),
  run_sha256              TEXT NOT NULL CHECK (
    length(run_sha256) = 64
    AND run_sha256 NOT GLOB '*[^0-9a-f]*'
  ),

  has_screenshot          INTEGER NOT NULL CHECK (has_screenshot IN (0, 1)),
  screenshot_content_type TEXT,
  screenshot_bytes        INTEGER,
  screenshot_sha256       TEXT,

  -- object_key embeds the primary key, so it is unique by construction and
  -- needs no unique index; this CHECK pins the structure instead.
  CHECK (object_key = 'bundles/' || substr(object_key, 9, 10) || '/' || bundle_id || '.bundle'),
  CHECK (
    (
      has_screenshot = 0
      AND screenshot_content_type IS NULL
      AND screenshot_bytes IS NULL
      AND screenshot_sha256 IS NULL
    )
    OR
    (
      has_screenshot = 1
      AND screenshot_content_type IS NOT NULL
      AND screenshot_content_type IN ('image/jpeg', 'image/webp')
      AND screenshot_bytes IS NOT NULL
      AND screenshot_bytes BETWEEN 1 AND 1048576
      AND screenshot_sha256 IS NOT NULL
      AND length(screenshot_sha256) = 64
      AND screenshot_sha256 NOT GLOB '*[^0-9a-f]*'
    )
  )
) WITHOUT ROWID;

CREATE INDEX idx_bundles_available
  ON bundles(available_at_ms, bundle_id, object_key);

-- GET /bazaardb/bundles pages only Screenshot-bearing Bundles.
CREATE INDEX idx_bundles_screenshot_available
  ON bundles(available_at_ms, bundle_id, object_key)
  WHERE has_screenshot = 1;

CREATE INDEX idx_bundles_stored_retention
  ON bundles(stored_at_ms, bundle_id);

CREATE TABLE ghost_battle_summaries (
  uploader_account_id TEXT NOT NULL,
  battle_id           TEXT NOT NULL,
  bundle_id           TEXT NOT NULL REFERENCES bundles(bundle_id) ON DELETE CASCADE,
  opponent_account_id TEXT NOT NULL,
  recorded_at_ms      INTEGER NOT NULL,
  is_final_battle     INTEGER NOT NULL DEFAULT 0 CHECK (is_final_battle IN (0, 1)),
  day                 INTEGER NOT NULL,
  hour                INTEGER NOT NULL,
  result              TEXT NOT NULL,
  winner_combatant_id TEXT NOT NULL,
  player_display_name TEXT NOT NULL,
  player_hero_name    TEXT,
  opponent_hero_name  TEXT,
  player_rank         TEXT,
  player_rating       INTEGER,
  PRIMARY KEY (uploader_account_id, battle_id)
) WITHOUT ROWID;

CREATE INDEX idx_ghost_summaries_query
  ON ghost_battle_summaries(opponent_account_id, recorded_at_ms DESC, battle_id DESC);

CREATE INDEX idx_ghost_summaries_bundle
  ON ghost_battle_summaries(bundle_id);

CREATE TABLE bundle_uploaders (
  player_account_id  TEXT PRIMARY KEY,
  first_bundle_at_ms INTEGER NOT NULL
) WITHOUT ROWID;
