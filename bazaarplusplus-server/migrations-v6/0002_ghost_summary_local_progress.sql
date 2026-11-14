-- Ghost discovery returns the queried local player's level, prestige, and
-- victories from the uploaded Battle projection, so the Mod can tell which of
-- its own Runs a Ghost Battle fought (ADR 0003). Rows stored before this
-- migration keep SQL NULL; retention replaces them within the Ghost window.
ALTER TABLE ghost_battle_summaries ADD COLUMN opponent_level INTEGER;
ALTER TABLE ghost_battle_summaries ADD COLUMN opponent_prestige INTEGER;
ALTER TABLE ghost_battle_summaries ADD COLUMN opponent_victories INTEGER;
