-- The runner deletes legacy rows in bounded batches first. Never drop a full table.
CREATE TABLE ghost_migration_gate (ok INTEGER NOT NULL CHECK (ok = 1));
INSERT INTO ghost_migration_gate VALUES (
  CASE WHEN NOT EXISTS (SELECT 1 FROM ghost_battles)
    AND (SELECT phase FROM ghost_projection_migration WHERE id = 1) = 'retired'
  THEN 1 ELSE 0 END
);
DROP TABLE ghost_migration_gate;
DROP TABLE ghost_battles;
DROP TABLE ghost_projection_migration;
