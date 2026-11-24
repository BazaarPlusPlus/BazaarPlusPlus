-- BPP Accounts, device tokens, verification challenges, web sessions, and durable Run Summaries.
-- run_summaries intentionally has no foreign key to bundles so 15-day Bundle retention
-- never cascades into long-lived account history (ADR 0002, ADR 0006).
PRAGMA foreign_keys = ON;

CREATE TABLE bpp_accounts (
  game_account_id TEXT PRIMARY KEY,
  email           TEXT NOT NULL UNIQUE,
  user_name       TEXT NOT NULL,
  created_at_ms   INTEGER NOT NULL,
  updated_at_ms   INTEGER NOT NULL
) WITHOUT ROWID;

CREATE TABLE bpp_account_bind_challenges (
  pending_id      TEXT PRIMARY KEY,
  game_account_id TEXT NOT NULL,
  email           TEXT NOT NULL,
  user_name       TEXT NOT NULL,
  code_hmac       TEXT NOT NULL,
  attempts        INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
  created_at_ms   INTEGER NOT NULL,
  expires_at_ms   INTEGER NOT NULL,
  consumed_at_ms  INTEGER
) WITHOUT ROWID;

CREATE INDEX idx_bind_challenges_email_created
  ON bpp_account_bind_challenges(email, created_at_ms DESC);

CREATE INDEX idx_bind_challenges_expires
  ON bpp_account_bind_challenges(expires_at_ms);

CREATE TABLE bpp_device_tokens (
  token_hash      TEXT PRIMARY KEY CHECK (
    length(token_hash) = 64
    AND token_hash NOT GLOB '*[^0-9a-f]*'
  ),
  game_account_id TEXT NOT NULL REFERENCES bpp_accounts(game_account_id) ON DELETE CASCADE,
  created_at_ms   INTEGER NOT NULL,
  last_used_at_ms INTEGER
) WITHOUT ROWID;

CREATE INDEX idx_device_tokens_account
  ON bpp_device_tokens(game_account_id);

CREATE TABLE bpp_web_login_challenges (
  challenge_id    TEXT PRIMARY KEY,
  email           TEXT NOT NULL,
  code_hmac       TEXT NOT NULL,
  attempts        INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
  created_at_ms   INTEGER NOT NULL,
  expires_at_ms   INTEGER NOT NULL,
  consumed_at_ms  INTEGER
) WITHOUT ROWID;

CREATE INDEX idx_web_login_email_created
  ON bpp_web_login_challenges(email, created_at_ms DESC);

CREATE INDEX idx_web_login_expires
  ON bpp_web_login_challenges(expires_at_ms);

CREATE TABLE bpp_web_sessions (
  session_hash    TEXT PRIMARY KEY CHECK (
    length(session_hash) = 64
    AND session_hash NOT GLOB '*[^0-9a-f]*'
  ),
  game_account_id TEXT NOT NULL REFERENCES bpp_accounts(game_account_id) ON DELETE CASCADE,
  created_at_ms   INTEGER NOT NULL,
  expires_at_ms   INTEGER NOT NULL
) WITHOUT ROWID;

CREATE INDEX idx_web_sessions_account
  ON bpp_web_sessions(game_account_id);

CREATE INDEX idx_web_sessions_expires
  ON bpp_web_sessions(expires_at_ms);

CREATE TABLE run_summaries (
  run_id                    TEXT PRIMARY KEY,
  game_account_id           TEXT NOT NULL,
  status                    TEXT,
  game_mode                 TEXT,
  hero                      TEXT,
  started_at_ms             INTEGER,
  ended_at_ms               INTEGER,
  day                       INTEGER,
  victories                 INTEGER,
  losses                    INTEGER,
  final_player_rank         TEXT,
  final_player_rating       INTEGER,
  final_player_rating_delta INTEGER,
  stored_at_ms              INTEGER NOT NULL
) WITHOUT ROWID;

CREATE INDEX idx_run_summaries_account_ended
  ON run_summaries(game_account_id, ended_at_ms DESC, run_id DESC);
