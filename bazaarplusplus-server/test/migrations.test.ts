import { env } from "cloudflare:test";
import { expect, test } from "vitest";

import { commitBundle } from "../src/modules/bundle-commit";
import { bundleData } from "./fixtures/bundle";

test("the hot-path migration backfills legacy deliveries and maintains Bundle storage ownership", async () => {
  // Restore the deployed initial schema to exercise the actual upgrade with data.
  const initial = env.TEST_MIGRATIONS[0];
  const legacyGhost = initial.queries.filter((sql) =>
    /CREATE (TABLE ghost_battles|INDEX idx_ghost_battles_query)/.test(sql),
  );
  await env.DB.batch(legacyGhost.map((sql) => env.DB.prepare(sql)));
  await env.DB.batch([
    env.DB.prepare("DROP TRIGGER bazaardb_delivery_storage_insert"),
    env.DB.prepare("DROP TRIGGER bundle_storage_update"),
    env.DB.prepare("DROP TRIGGER bazaardb_delivery_storage_requeue"),
    env.DB.prepare("DROP INDEX idx_bazaardb_pending_retention"),
    env.DB.prepare("ALTER TABLE bazaardb_deliveries DROP COLUMN bundle_stored_at_ms"),
  ]);
  const fixture = await bundleData({
    bundleId: "01J00000000000000000000901",
    runId: "migration-legacy-run",
    screenshotBytes: new Uint8Array([1, 2, 3]),
    battles: [],
  });
  await commitBundle(env.DB, fixture.descriptor, fixture.digest, {
    storedAtMs: 19,
    availableAtMs: 20,
  });
  const terminal = await bundleData({
    bundleId: "01J00000000000000000000902",
    runId: "migration-terminal-run",
    screenshotBytes: new Uint8Array([4, 5, 6]),
    battles: [],
  });
  await commitBundle(env.DB, terminal.descriptor, terminal.digest, {
    storedAtMs: 39,
    availableAtMs: 40,
  });
  await env.DB.prepare(
    "UPDATE bazaardb_deliveries SET delivery_state = 'done', delivered_at_ms = 41 WHERE bundle_id = ?1",
  )
    .bind(terminal.descriptor.bundleId)
    .run();
  const migration = env.TEST_MIGRATIONS.find(({ name }) => name === "0002_hot_path_indexes.sql");
  if (migration === undefined) throw new Error("Missing hot-path migration");
  await env.DB.batch(migration.queries.map((sql) => env.DB.prepare(sql)));
  const stored = () =>
    env.DB.prepare("SELECT bundle_stored_at_ms FROM bazaardb_deliveries WHERE bundle_id = ?1")
      .bind(fixture.descriptor.bundleId)
      .first();
  expect(await stored()).toEqual({ bundle_stored_at_ms: 19 });
  await env.DB.prepare("UPDATE bundles SET stored_at_ms = 25 WHERE bundle_id = ?1")
    .bind(fixture.descriptor.bundleId)
    .run();
  expect(await stored()).toEqual({ bundle_stored_at_ms: 25 });

  expect(
    await env.DB.prepare("SELECT bundle_stored_at_ms FROM bazaardb_deliveries WHERE bundle_id = ?1")
      .bind(terminal.descriptor.bundleId)
      .first(),
  ).toEqual({ bundle_stored_at_ms: 0 });
  await env.DB.prepare(
    "UPDATE bazaardb_deliveries SET delivery_state = 'pending', delivered_at_ms = NULL WHERE bundle_id = ?1",
  )
    .bind(terminal.descriptor.bundleId)
    .run();
  expect(
    await env.DB.prepare("SELECT bundle_stored_at_ms FROM bazaardb_deliveries WHERE bundle_id = ?1")
      .bind(terminal.descriptor.bundleId)
      .first(),
  ).toEqual({ bundle_stored_at_ms: 39 });

  // Old Worker INSERTs omit the projection; the trigger still fills it atomically.
  await env.DB.prepare("DELETE FROM bazaardb_deliveries WHERE bundle_id = ?1")
    .bind(fixture.descriptor.bundleId)
    .run();
  await env.DB.prepare(
    `INSERT INTO bazaardb_deliveries (bundle_id, claimable_at_ms, created_at_ms, state_updated_at_ms)
     VALUES (?1, 30, 30, 30)`,
  )
    .bind(fixture.descriptor.bundleId)
    .run();
  expect(await stored()).toEqual({ bundle_stored_at_ms: 25 });
});
