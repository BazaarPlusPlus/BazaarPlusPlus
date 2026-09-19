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
  bundle_version          INTEGER NOT NULL CHECK (bundle_version = 5),
  manifest_bytes          INTEGER NOT NULL CHECK (manifest_bytes BETWEEN 1 AND 2097152),
  object_bytes            INTEGER NOT NULL CHECK (object_bytes BETWEEN 1 AND 8388607),
  client_created_at_ms    INTEGER NOT NULL,
  stored_at_ms            INTEGER NOT NULL,
  available_at_ms         INTEGER NOT NULL,

  run_format_version      INTEGER NOT NULL CHECK (run_format_version = 5),
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

CREATE INDEX idx_bundles_stored_retention
  ON bundles(stored_at_ms, bundle_id);

CREATE TABLE ghost_battles (
  uploader_account_id TEXT NOT NULL,
  battle_id            TEXT NOT NULL,
  bundle_id            TEXT NOT NULL REFERENCES bundles(bundle_id) ON DELETE CASCADE,
  opponent_account_id  TEXT NOT NULL,
  recorded_at_ms       INTEGER NOT NULL,
  is_final_battle      INTEGER NOT NULL DEFAULT 0 CHECK (is_final_battle IN (0, 1)),
  projection_json      TEXT NOT NULL CHECK (json_valid(projection_json)),
  PRIMARY KEY (uploader_account_id, battle_id)
) WITHOUT ROWID;

CREATE INDEX idx_ghost_battles_query
  ON ghost_battles(opponent_account_id, recorded_at_ms DESC, battle_id DESC);

CREATE TABLE bundle_uploaders (
  player_account_id  TEXT PRIMARY KEY,
  first_bundle_at_ms INTEGER NOT NULL
) WITHOUT ROWID;

CREATE TABLE bazaardb_deliveries (
  bundle_id           TEXT PRIMARY KEY REFERENCES bundles(bundle_id) ON DELETE CASCADE,
  delivery_state      TEXT NOT NULL DEFAULT 'pending'
    CHECK (delivery_state IN ('pending', 'done', 'failed')),
  active_claim_id     TEXT,
  active_claim_order  INTEGER CHECK (
    active_claim_order IS NULL
    OR (
      typeof(active_claim_order) = 'integer'
      AND active_claim_order BETWEEN 0 AND 49
    )
  ),
  claimable_at_ms     INTEGER NOT NULL,
  delivery_attempts   INTEGER NOT NULL DEFAULT 0
    CHECK (delivery_attempts BETWEEN 0 AND 3),
  created_at_ms       INTEGER NOT NULL,
  state_updated_at_ms INTEGER NOT NULL,
  delivered_at_ms     INTEGER,
  failed_at_ms        INTEGER,
  failure_reason      TEXT,
  CHECK (
    (active_claim_id IS NULL AND active_claim_order IS NULL)
    OR (
      active_claim_id IS NOT NULL
      AND active_claim_order IS NOT NULL
      AND delivery_state = 'pending'
    )
  ),
  CHECK (
    (
      delivery_state = 'pending'
      AND delivered_at_ms IS NULL
      AND failed_at_ms IS NULL
      AND failure_reason IS NULL
    )
    OR (
      delivery_state = 'done'
      AND delivered_at_ms IS NOT NULL
      AND failed_at_ms IS NULL
      AND failure_reason IS NULL
    )
    OR (
      delivery_state = 'failed'
      AND failed_at_ms IS NOT NULL
      AND delivered_at_ms IS NULL
      AND failure_reason IS NOT NULL
    )
  )
) WITHOUT ROWID;

CREATE INDEX idx_bazaardb_claimable
  ON bazaardb_deliveries(claimable_at_ms, created_at_ms, bundle_id)
  WHERE delivery_state = 'pending' AND delivery_attempts < 3;

CREATE INDEX idx_bazaardb_active_claim_order
  ON bazaardb_deliveries(active_claim_id, active_claim_order, bundle_id)
  WHERE delivery_state = 'pending' AND active_claim_id IS NOT NULL;

CREATE INDEX idx_bazaardb_exhausted_lease
  ON bazaardb_deliveries(claimable_at_ms, bundle_id)
  WHERE delivery_state = 'pending'
    AND delivery_attempts = 3
    AND active_claim_id IS NOT NULL;

CREATE TABLE bazaardb_delivery_attempts (
  claim_id       TEXT NOT NULL,
  bundle_id      TEXT NOT NULL REFERENCES bazaardb_deliveries(bundle_id) ON DELETE CASCADE,
  attempt_number INTEGER NOT NULL CHECK (attempt_number BETWEEN 1 AND 3),
  claimed_at_ms  INTEGER NOT NULL,
  expires_at_ms  INTEGER NOT NULL,
  settled_at_ms  INTEGER,
  outcome        TEXT CHECK (
    outcome IS NULL OR outcome IN (
      'accepted', 'retryable_failure', 'permanent_failure'
    )
  ),
  reason         TEXT,
  PRIMARY KEY (claim_id, bundle_id),
  UNIQUE (bundle_id, attempt_number),
  CHECK (
    (outcome IS NULL AND settled_at_ms IS NULL AND reason IS NULL)
    OR (outcome = 'accepted' AND settled_at_ms IS NOT NULL AND reason IS NULL)
    OR (
      outcome IN ('retryable_failure', 'permanent_failure')
      AND settled_at_ms IS NOT NULL
      AND reason IS NOT NULL
    )
  )
) WITHOUT ROWID;
