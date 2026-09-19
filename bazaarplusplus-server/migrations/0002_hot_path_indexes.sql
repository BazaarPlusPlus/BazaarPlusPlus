-- This projection is owned by database triggers so existing Workers and
-- Bundle storage updates preserve the authoritative retention time for pending rows.
ALTER TABLE bazaardb_deliveries
  ADD COLUMN bundle_stored_at_ms INTEGER NOT NULL DEFAULT 0;

CREATE INDEX idx_bazaardb_pending_retention
  ON bazaardb_deliveries(bundle_stored_at_ms, bundle_id)
  WHERE delivery_state = 'pending';

-- Terminal history does not participate in retention. Backfill only pending rows
-- and populate the projection if an operator later requeues a terminal delivery.
UPDATE bazaardb_deliveries INDEXED BY idx_bazaardb_pending_retention
SET bundle_stored_at_ms = (
  SELECT stored_at_ms FROM bundles
  WHERE bundles.bundle_id = bazaardb_deliveries.bundle_id
)
WHERE delivery_state = 'pending';

CREATE TRIGGER bazaardb_delivery_storage_insert
AFTER INSERT ON bazaardb_deliveries
BEGIN
  UPDATE bazaardb_deliveries
  SET bundle_stored_at_ms = (
    SELECT stored_at_ms FROM bundles WHERE bundle_id = NEW.bundle_id
  )
  WHERE bundle_id = NEW.bundle_id;
END;

CREATE TRIGGER bundle_storage_update
AFTER UPDATE OF stored_at_ms ON bundles
WHEN OLD.stored_at_ms <> NEW.stored_at_ms
BEGIN
  UPDATE bazaardb_deliveries
  SET bundle_stored_at_ms = NEW.stored_at_ms
  WHERE bundle_id = NEW.bundle_id;
END;

CREATE TRIGGER bazaardb_delivery_storage_requeue
AFTER UPDATE OF delivery_state ON bazaardb_deliveries
WHEN NEW.delivery_state = 'pending' AND OLD.delivery_state <> 'pending'
BEGIN
  UPDATE bazaardb_deliveries
  SET bundle_stored_at_ms = (
    SELECT stored_at_ms FROM bundles WHERE bundle_id = NEW.bundle_id
  )
  WHERE bundle_id = NEW.bundle_id;
END;

CREATE INDEX idx_ghost_battles_bundle
  ON ghost_battles(bundle_id);
