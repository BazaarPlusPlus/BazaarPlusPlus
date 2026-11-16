-- Per-account candidates; the Bot filters decoded Run end times.
CREATE INDEX idx_bundles_uploader_stored
  ON bundles(uploader_account_id, stored_at_ms, bundle_id);
CREATE INDEX idx_ghost_player_name
  ON ghost_battle_summaries(player_display_name, uploader_account_id, bundle_id);
