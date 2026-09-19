import { createScheduledController, env } from "cloudflare:test";
import { afterEach, beforeEach, expect, test, vi } from "vitest";

import worker from "../../src/index";
import {
  D1_RETENTION_BATCH_SIZE,
  D1_RETENTION_MAX_BATCHES,
  D1_RETENTION_MS,
} from "../../src/limits";
import { pruneExpiredBundles } from "../../src/modules/d1-retention";
import { seedDeliveryBacklog } from "../fixtures/backlog";
import { recordD1 } from "../fixtures/d1";

const NOW = 1_785_628_800_000;
const CUTOFF = NOW - D1_RETENTION_MS;
const id = (index: number) => `01J9${String(index).padStart(22, "0")}`;

beforeEach(async () => {
  await env.DB.prepare("DELETE FROM bundles").run();
});
afterEach(() => vi.restoreAllMocks());

test("scheduled retention uses stored time, cascades children and preserves uploaders", async () => {
  await seedDeliveryBacklog(env.DB, 3, CUTOFF - 1);
  await env.DB.batch([
    env.DB.prepare("UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2").bind(CUTOFF, id(1)),
    env.DB.prepare("UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2").bind(
      CUTOFF + 1,
      id(2),
    ),
    env.DB.prepare("UPDATE bundles SET client_created_at_ms = ?1 WHERE bundle_id = ?2").bind(
      NOW,
      id(0),
    ),
    env.DB.prepare(`INSERT INTO bundle_uploaders (player_account_id, first_bundle_at_ms)
      VALUES ('retained-uploader', ?1)`).bind(CUTOFF - 1),
    env.DB.prepare(`INSERT INTO ghost_battle_summaries (
      uploader_account_id, battle_id, bundle_id, opponent_account_id, recorded_at_ms, day, hour, result, player_display_name
    ) SELECT 'retained-uploader', run_id, bundle_id, 'retained-uploader', stored_at_ms, 1, 1, 'win', 'Uploader'
      FROM bundles`),
    env.DB.prepare(`INSERT INTO bazaardb_delivery_attempts (
      claim_id, bundle_id, attempt_number, claimed_at_ms, expires_at_ms
    ) SELECT 'old-claim', bundle_id, 1, ?1, ?1 + 600000 FROM bundles`).bind(CUTOFF - 1),
  ]);

  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);
  await worker.scheduled(createScheduledController({ scheduledTime: NOW }), {
    DB: env.DB,
  } as Cloudflare.Env);
  for (const table of [
    "bundles",
    "ghost_battle_summaries",
    "bazaardb_deliveries",
    "bazaardb_delivery_attempts",
  ]) {
    const rows = await env.DB.prepare(`SELECT bundle_id FROM ${table} ORDER BY bundle_id`).all();
    expect(rows.results).toEqual([{ bundle_id: id(1) }, { bundle_id: id(2) }]);
  }
  expect(
    await env.DB.prepare(
      "SELECT first_bundle_at_ms FROM bundle_uploaders WHERE player_account_id = ?1",
    )
      .bind("retained-uploader")
      .first(),
  ).toEqual({ first_bundle_at_ms: CUTOFF - 1 });
  expect(log).toHaveBeenCalledWith(expect.stringContaining('"deleted_bundles":1'));
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({ deleted: 0, hasMore: false });
  expect(await pruneExpiredBundles(env.DB, NOW + 1)).toEqual({ deleted: 1, hasMore: false });
});

test("retention has bounded indexed batches and resumes the remaining backlog", async () => {
  const budget = D1_RETENTION_BATCH_SIZE * D1_RETENTION_MAX_BATCHES;
  await seedDeliveryBacklog(env.DB, budget + 3, CUTOFF - 1);
  const queries = recordD1(env.DB);
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({ deleted: budget, hasMore: true });
  expect(queries).toHaveLength(D1_RETENTION_MAX_BATCHES);
  for (const query of queries) {
    expect(query.result.results).toHaveLength(D1_RETENTION_BATCH_SIZE);
    expect(query.result.meta.rows_read).toBeLessThan(D1_RETENTION_BATCH_SIZE * 20);
  }
  const query = queries[0];
  const plan = await env.DB.prepare(`EXPLAIN QUERY PLAN ${query.sql}`)
    .bind(...query.bindings)
    .all<{ detail: string }>();
  const detail = plan.results.map(({ detail }) => detail).join("\n");
  expect(detail).toContain("idx_bundles_stored_retention");
  expect(detail).toContain("idx_ghost_summaries_bundle");
  expect(detail).not.toContain("TEMP B-TREE");
  expect(detail).not.toContain("SCAN ghost_battle_summaries");
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({ deleted: 3, hasMore: false });
});

test("a failed batch preserves earlier progress and fails the scheduled invocation", async () => {
  await seedDeliveryBacklog(env.DB, D1_RETENTION_BATCH_SIZE + 1, CUTOFF - 1);
  await env.DB.prepare(`CREATE TRIGGER reject_retention_probe BEFORE DELETE ON bundles
    WHEN OLD.bundle_id = '${id(D1_RETENTION_BATCH_SIZE)}'
    BEGIN SELECT RAISE(ABORT, 'injected retention failure'); END`).run();
  const log = vi.spyOn(console, "error").mockImplementation(() => undefined);
  try {
    await expect(
      worker.scheduled(createScheduledController({ scheduledTime: NOW }), {
        DB: env.DB,
      } as Cloudflare.Env),
    ).rejects.toThrow("injected retention failure");
    expect(log).toHaveBeenCalledWith(expect.stringContaining('"event":"d1.retention.failed"'));
    expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM bundles").first()).toEqual({
      count: 1,
    });
  } finally {
    await env.DB.prepare("DROP TRIGGER reject_retention_probe").run();
  }
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({ deleted: 1, hasMore: false });
});
