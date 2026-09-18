-- Only run after the summary Worker serves all traffic and old requests drain.
CREATE TABLE ghost_migration_gate (ok INTEGER NOT NULL CHECK (ok = 1));
INSERT INTO ghost_migration_gate VALUES (CASE WHEN
  (SELECT phase FROM ghost_projection_migration WHERE id = 1) = 'bridge'
  AND ((SELECT retire_authorized FROM ghost_projection_migration WHERE id = 1) = 1
    OR NOT EXISTS (SELECT 1 FROM bundles)) THEN 1 ELSE 0 END);
DROP TABLE ghost_migration_gate;

DROP TRIGGER ghost_summary_to_legacy;
DROP TRIGGER ghost_legacy_to_summary;
DROP TRIGGER ghost_legacy_duplicate;
-- Any late legacy writer fails its entire Bundle transaction and can retry.
CREATE TRIGGER ghost_legacy_retired BEFORE INSERT ON ghost_battles
BEGIN SELECT RAISE(ABORT, 'Legacy Ghost writer retired'); END;
UPDATE ghost_projection_migration SET phase = 'retired' WHERE id = 1;
