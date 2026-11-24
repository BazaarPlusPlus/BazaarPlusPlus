-- Site-first BPP Accounts (ADR 0007): an account is an email and a password,
-- optionally linked to one game account through a Bind Code. The 0005 account
-- tables were empty in production when this replaced them.
PRAGMA foreign_keys = ON;

DROP TABLE bpp_device_tokens;
DROP TABLE bpp_web_sessions;
DROP TABLE bpp_account_bind_challenges;
DROP TABLE bpp_web_login_challenges;
DROP TABLE bpp_accounts;

CREATE TABLE bpp_accounts (
  account_id      TEXT PRIMARY KEY,
  email           TEXT NOT NULL UNIQUE,
  password_hash   TEXT NOT NULL,
  game_account_id TEXT UNIQUE,
  game_user_name  TEXT,
  linked_at_ms    INTEGER,
  created_at_ms   INTEGER NOT NULL,
  updated_at_ms   INTEGER NOT NULL
) WITHOUT ROWID;

CREATE TABLE bpp_email_challenges (
  challenge_id    TEXT PRIMARY KEY,
  purpose         TEXT NOT NULL CHECK (purpose IN ('register', 'reset')),
  email           TEXT NOT NULL,
  code_hmac       TEXT NOT NULL,
  attempts        INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
  created_at_ms   INTEGER NOT NULL,
  expires_at_ms   INTEGER NOT NULL,
  consumed_at_ms  INTEGER
) WITHOUT ROWID;

CREATE INDEX idx_email_challenges_email_purpose_created
  ON bpp_email_challenges(email, purpose, created_at_ms DESC);

CREATE INDEX idx_email_challenges_expires
  ON bpp_email_challenges(expires_at_ms);

CREATE TABLE bpp_bind_codes (
  code_hash       TEXT PRIMARY KEY CHECK (
    length(code_hash) = 64
    AND code_hash NOT GLOB '*[^0-9a-f]*'
  ),
  account_id      TEXT NOT NULL UNIQUE REFERENCES bpp_accounts(account_id) ON DELETE CASCADE,
  created_at_ms   INTEGER NOT NULL,
  expires_at_ms   INTEGER NOT NULL
) WITHOUT ROWID;

CREATE INDEX idx_bind_codes_expires
  ON bpp_bind_codes(expires_at_ms);

CREATE TABLE bpp_device_tokens (
  token_hash      TEXT PRIMARY KEY CHECK (
    length(token_hash) = 64
    AND token_hash NOT GLOB '*[^0-9a-f]*'
  ),
  account_id      TEXT NOT NULL REFERENCES bpp_accounts(account_id) ON DELETE CASCADE,
  game_account_id TEXT NOT NULL,
  created_at_ms   INTEGER NOT NULL,
  last_used_at_ms INTEGER
) WITHOUT ROWID;

CREATE INDEX idx_device_tokens_account
  ON bpp_device_tokens(account_id);

CREATE TABLE bpp_web_sessions (
  session_hash    TEXT PRIMARY KEY CHECK (
    length(session_hash) = 64
    AND session_hash NOT GLOB '*[^0-9a-f]*'
  ),
  account_id      TEXT NOT NULL REFERENCES bpp_accounts(account_id) ON DELETE CASCADE,
  created_at_ms   INTEGER NOT NULL,
  expires_at_ms   INTEGER NOT NULL
) WITHOUT ROWID;

CREATE INDEX idx_web_sessions_account
  ON bpp_web_sessions(account_id);

CREATE INDEX idx_web_sessions_expires
  ON bpp_web_sessions(expires_at_ms);

-- 1 when the Run segment was copied to account-runs/<account_id>/<run_id>.run.
ALTER TABLE run_summaries ADD COLUMN detail_stored INTEGER NOT NULL DEFAULT 0;
