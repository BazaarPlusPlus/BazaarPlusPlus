-- Ghost discovery signs each row's download URL from its parent Bundle's
-- object key. Copying the key onto the Ghost row lets the hot discovery query
-- read one table instead of joining every row back to bundles (ADR 0003).
-- Adding a nullable column only rewrites the schema, so this migration does
-- not scan or lock the table. Rows stored before it keep SQL NULL and fall back
-- to the parent Bundle's key; retention replaces them within the Ghost window.
ALTER TABLE ghost_battle_summaries ADD COLUMN object_key TEXT;
