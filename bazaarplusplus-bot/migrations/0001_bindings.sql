CREATE TABLE bindings (
  member_openid TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  display_name TEXT NOT NULL
) WITHOUT ROWID;
CREATE INDEX idx_bindings_account ON bindings(account_id);
CREATE INDEX idx_bindings_name ON bindings(display_name, account_id);
