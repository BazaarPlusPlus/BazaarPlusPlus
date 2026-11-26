-- A linked BPP Account's records are its last 8 days of Bundles (ADR 0008).
-- Each Bundle row carries the terminal `projection.run` facts and the absolute
-- byte range of its Run segment; rows from before this migration, and Bundles
-- from mods that write an empty `projection.run`, keep them NULL and are not
-- records. Run Summaries, their R2 copies, and Device Tokens are gone; both
-- tables were empty in production when this dropped them.
PRAGMA foreign_keys = ON;

ALTER TABLE bundles ADD COLUMN hero TEXT;
ALTER TABLE bundles ADD COLUMN victories INTEGER;
ALTER TABLE bundles ADD COLUMN losses INTEGER;
ALTER TABLE bundles ADD COLUMN final_player_rank TEXT;
ALTER TABLE bundles ADD COLUMN final_player_rating INTEGER;
ALTER TABLE bundles ADD COLUMN final_player_rating_delta INTEGER;
ALTER TABLE bundles ADD COLUMN ended_at_ms INTEGER;
ALTER TABLE bundles ADD COLUMN run_offset INTEGER;
ALTER TABLE bundles ADD COLUMN run_length INTEGER;

DROP TABLE run_summaries;
DROP TABLE bpp_device_tokens;
