import { env } from "cloudflare:test";
import { afterEach, beforeEach, expect, test, vi } from "vitest";

import { DELIVERY_MAINTENANCE_BATCH_SIZE, R2_RETENTION_MS } from "../../src/limits";
import { claimDeliveries, settleDeliveries } from "../../src/modules/bazaardb-delivery";
import { seedDeliveryBacklog } from "../fixtures/backlog";
import { type RecordedD1Query, recordD1 } from "../fixtures/d1";
import { createTestDeps } from "../fixtures/deps";

const NOW = 1_785_628_800_000;
const deps = () => createTestDeps({ now: () => NOW });
const request = (body: unknown) =>
  new Request("https://worker.test/", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
const claim = (limit = 50) => claimDeliveries(request({ limit }), env, "cost-claim", deps());
function findQuery(queries: RecordedD1Query[], text: string): RecordedD1Query {
  const found = queries.filter(({ sql }) => sql.includes(text));
  expect(found).toHaveLength(1);
  return found[0];
}
beforeEach(async () => {
  await env.DB.prepare("DELETE FROM bundles").run();
});
afterEach(() => vi.restoreAllMocks());

test("large expiry backlogs make bounded progress without claiming or scanning remaining candidates", async () => {
  await seedDeliveryBacklog(env.DB, 20000, NOW - R2_RETENTION_MS - 1);
  const queries = recordD1(env.DB);
  await expect(claim(1)).rejects.toMatchObject({
    status: 503,
    code: "storage_unavailable",
    retryable: true,
    headers: { "Retry-After": "1" },
  });
  const expiry = findQuery(queries, "failure_reason = 'bundle_expired'");
  expect(expiry.result.meta.changes).toBe(DELIVERY_MAINTENANCE_BATCH_SIZE);
  expect(expiry.result.meta.rows_read).toBeLessThan(DELIVERY_MAINTENANCE_BATCH_SIZE * 6);
  const claimed = findQuery(queries, "WITH candidates");
  expect(claimed.result.meta.changes).toBe(0);
  expect(claimed.result.meta.rows_read).toBeLessThan(10);
  expect(
    await env.DB.prepare("SELECT COUNT(*) AS count FROM bazaardb_delivery_attempts").first(),
  ).toEqual({ count: 0 });
});

test("claim resumes when expiry catches up and preserves expiry priority over exhausted attempts", async () => {
  await seedDeliveryBacklog(env.DB, DELIVERY_MAINTENANCE_BATCH_SIZE + 3, NOW - R2_RETENTION_MS - 1);
  const expiredId = `01J9${String(DELIVERY_MAINTENANCE_BATCH_SIZE).padStart(22, "0")}`;
  const liveId = `01J9${String(DELIVERY_MAINTENANCE_BATCH_SIZE + 2).padStart(22, "0")}`;
  await env.DB.prepare("UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2")
    .bind(NOW, liveId)
    .run();
  await env.DB.prepare(`UPDATE bazaardb_deliveries
    SET delivery_attempts = 3, active_claim_id = 'exhausted', active_claim_order = 0
    WHERE bundle_id = ?1`)
    .bind(expiredId)
    .run();
  await expect(claim()).rejects.toMatchObject({ status: 503 });
  expect(
    await env.DB.prepare("SELECT failure_reason FROM bazaardb_deliveries WHERE bundle_id = ?1")
      .bind(expiredId)
      .first(),
  ).toEqual({ failure_reason: "bundle_expired" });
  const result = await claim();
  expect(result.items).toMatchObject([{ bundle_id: liveId }]);
});

test("exhausted attempts also converge in bounded pages", async () => {
  await seedDeliveryBacklog(env.DB, DELIVERY_MAINTENANCE_BATCH_SIZE + 1, NOW - 120000);
  await env.DB.prepare(`UPDATE bazaardb_deliveries
    SET delivery_attempts = 3, active_claim_id = 'exhausted', active_claim_order = 0`).run();
  const queries = recordD1(env.DB);
  expect((await claim()).items).toEqual([]);
  const exhausted = findQuery(queries, "ELSE 'delivery_attempts_exhausted'");
  expect(exhausted.result.meta.changes).toBe(DELIVERY_MAINTENANCE_BATCH_SIZE);
  expect(exhausted.result.meta.rows_read).toBeLessThan(DELIVERY_MAINTENANCE_BATCH_SIZE * 6);
  expect((await claim()).items).toEqual([]);
  expect(
    await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM bazaardb_deliveries WHERE delivery_state = 'pending'",
    ).first(),
  ).toEqual({ count: 0 });
});

test.each([1, 10, 50])(
  "claim cost scales with a page of %i and settle reads receipts together",
  async (limit) => {
    await seedDeliveryBacklog(env.DB, 20000, NOW - 120000);
    const queries = recordD1(env.DB);
    const result = await claim(limit);
    const claimed = findQuery(queries, "WITH candidates");
    expect(claimed.result.meta.rows_read).toBeLessThan(limit * 12 + 10);
    const plan = await env.DB.prepare(`EXPLAIN QUERY PLAN ${claimed.sql}`)
      .bind(...claimed.bindings)
      .all<{ detail: string }>();
    expect(plan.results.map(({ detail }) => detail).join("\n")).not.toContain(
      "CORRELATED SCALAR SUBQUERY",
    );
    const offset = queries.length;
    const results = (result.items as { bundle_id: string }[])
      .slice()
      .reverse()
      .map(({ bundle_id }) => ({ bundle_id, outcome: "accepted" }));
    const settle = () =>
      settleDeliveries(request({ claim_id: result.claim_id, results }), env, "cost-settle", deps());
    const settled = await settle();
    expect(settled.summary).toEqual({ applied: limit, duplicate: 0, rejected: 0 });
    expect((settled.items as { bundle_id: string }[]).map(({ bundle_id }) => bundle_id)).toEqual(
      results.map(({ bundle_id }) => bundle_id),
    );
    expect(queries.slice(offset)).toHaveLength(2 * limit + 1);
    expect(queries.slice(offset).filter(({ sql }) => sql.trim().startsWith("SELECT"))).toHaveLength(
      1,
    );
    expect((await settle()).summary).toEqual({ applied: 0, duplicate: limit, rejected: 0 });
    console.log(
      JSON.stringify({
        event: "test.delivery_cost",
        limit,
        rows_read: claimed.result.meta.rows_read,
        settle_statements: 2 * limit + 1,
      }),
    );
  },
);
