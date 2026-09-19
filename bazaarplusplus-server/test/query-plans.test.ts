import { env } from "cloudflare:test";
import { afterEach, describe, expect, test, vi } from "vitest";

import { claimDeliveries, settleDeliveries } from "../src/modules/bazaardb-delivery";
import { collectBundles } from "../src/modules/bundle-collection";
import { discoverGhostBattles } from "../src/modules/ghost-battle-discovery";
import { seedDeliveryBacklog } from "./fixtures/backlog";
import { type RecordedD1Query, recordD1 } from "./fixtures/d1";
import { createTestDeps } from "./fixtures/deps";

const NOW = 1_785_628_800_000;
const AVAILABLE = NOW - 120_000;
const BUNDLE_ID = "01J00000000000000000000001";

function query(queries: RecordedD1Query[], pattern: RegExp): RecordedD1Query {
  const found = queries.filter(({ sql }) => pattern.test(sql.replace(/\s+/g, " ").trim()));
  expect(found, `Executed D1 query matching ${pattern}`).toHaveLength(1);
  return found[0];
}

async function plan({ sql, bindings }: Pick<RecordedD1Query, "sql" | "bindings">): Promise<string> {
  const result = await env.DB.prepare(`EXPLAIN QUERY PLAN ${sql}`)
    .bind(...bindings)
    .all<{ detail: string }>();
  return result.results.map(({ detail }) => detail).join("\n");
}

function collectionRequest(afterId?: string): Request {
  const url = new URL("https://worker.test/bundles");
  url.searchParams.set("available_from_ms", String(AVAILABLE));
  url.searchParams.set("limit", "50");
  if (afterId !== undefined) {
    url.searchParams.set("after_available_at_ms", String(AVAILABLE));
    url.searchParams.set("after_bundle_id", afterId);
  }
  return new Request(url);
}

function deliveryRequest(path: string, body: unknown): Request {
  return new Request(`https://worker.test/bazaardb/deliveries/${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

const deps = () => createTestDeps({ now: () => NOW });
afterEach(() => vi.restoreAllMocks());

describe("executed D1 query plans", () => {
  test.each([undefined, BUNDLE_ID])(
    "Bundle collection uses its covering index after %s",
    async (afterId) => {
      const queries = recordD1(env.DB);
      await collectBundles(collectionRequest(afterId), env, "plan-collection", deps());
      const detail = await plan(query(queries, /^SELECT .* FROM bundles /));
      expect(detail).toContain("idx_bundles_available");
      expect(detail).toContain("COVERING");
      expect(detail).not.toContain("TEMP B-TREE");
    },
  );

  test("Ghost discovery uses the opponent/time index and Bundle primary key", async () => {
    const queries = recordD1(env.DB);
    await discoverGhostBattles(
      new Request("https://worker.test/ghost-battles?player_account_id=plan-account"),
      { ...env, GHOST_BATTLE_RATE_LIMITER: { limit: async () => ({ success: true }) } },
      "plan-ghost",
      deps(),
    );
    const detail = await plan(query(queries, /^SELECT .* FROM ghost_battle_summaries /));
    expect(detail).toContain("idx_ghost_summaries_query");
    expect(detail).toMatch(/PRIMARY KEY|sqlite_autoindex_bundles_1/);
    expect(detail).not.toContain("TEMP B-TREE");
  });

  test("claim, receipts and convergence use their partial indexes", async () => {
    const queries = recordD1(env.DB);
    await claimDeliveries(deliveryRequest("claim", { limit: 50 }), env, "plan-claim", deps());
    const claim = await plan(query(queries, /^WITH /));
    expect(claim).toContain("idx_bazaardb_claimable");
    expect(claim).not.toContain("TEMP B-TREE");
    const attempts = await plan(query(queries, /^INSERT INTO bazaardb_delivery_attempts /));
    expect(attempts).toContain("idx_bazaardb_active_claim_order");
    const active = await plan(query(queries, /^SELECT b\.bundle_id/));
    expect(active).toContain("idx_bazaardb_active_claim_order");
    expect(active).not.toContain("TEMP B-TREE");
    const exhausted = await plan(query(queries, /^UPDATE .*ELSE 'delivery_attempts_exhausted'/));
    expect(exhausted).toContain("idx_bazaardb_exhausted_lease");
    const expired = await plan(query(queries, /^UPDATE .*failure_reason = 'bundle_expired'/));
    expect(expired).toContain(
      "SEARCH bazaardb_deliveries USING COVERING INDEX idx_bazaardb_pending_retention",
    );
    expect(expired).toContain("bundle_stored_at_ms<?");
    expect(expired).not.toContain("SCAN");
  });

  test("settle updates and receipt reads use primary keys", async () => {
    const queries = recordD1(env.DB);
    const result = await settleDeliveries(
      deliveryRequest("settle", {
        claim_id: "clm_550e8400-e29b-41d4-a716-446655440000",
        results: [{ bundle_id: BUNDLE_ID, outcome: "retryable_failure", reason: "timeout" }],
      }),
      env,
      "plan-settle",
      deps(),
    );
    expect(result.summary).toEqual({ applied: 0, duplicate: 0, rejected: 1 });
    for (const [pattern, table] of [
      [/^UPDATE bazaardb_delivery_attempts /, "bazaardb_delivery_attempts"],
      [/^UPDATE bazaardb_deliveries /, "bazaardb_deliveries"],
      [/^SELECT /, "a"],
    ] as const) {
      const detail = await plan(query(queries, pattern));
      expect(detail).toContain(`SEARCH ${table} USING PRIMARY KEY`);
    }
  });
});

describe("schema query plans", () => {
  test("attempt uniqueness uses its receipt index", async () => {
    const detail = await plan({
      sql: "SELECT claim_id FROM bazaardb_delivery_attempts WHERE bundle_id = ?1 AND attempt_number = ?2",
      bindings: [BUNDLE_ID, 1],
    });
    expect(detail).toContain("sqlite_autoindex_bazaardb_delivery_attempts_2");
  });

  test("Bundle deletion looks up Ghost children through their foreign-key index", async () => {
    const detail = await plan({
      sql: "DELETE FROM bundles WHERE bundle_id = ?1",
      bindings: [BUNDLE_ID],
    });
    expect(detail).toContain("idx_ghost_summaries_bundle");
    expect(detail).not.toContain("SCAN ghost_battle_summaries");
  });
});

test("keyset reads and empty expiry work stay bounded with a large live backlog", async () => {
  await seedDeliveryBacklog(env.DB, 2000, AVAILABLE, NOW + 60_000);

  const queries = recordD1(env.DB);
  const page = await collectBundles(
    collectionRequest(`01J9${String(1948).padStart(22, "0")}`),
    env,
    "cost-collection",
    deps(),
  );
  expect(page.items).toHaveLength(50);
  expect(page.next_after).toEqual({
    available_at_ms: AVAILABLE,
    bundle_id: `01J9${String(1998).padStart(22, "0")}`,
  });
  const collection = query(queries, /^SELECT .* FROM bundles /).result;
  expect(collection.results).toHaveLength(51);
  expect(collection.meta.rows_read).toBeLessThan(100);

  const claim = await claimDeliveries(deliveryRequest("claim", {}), env, "cost-expiry", deps());
  expect(claim.items).toEqual([]);
  const expiry = query(queries, /^UPDATE .*failure_reason = 'bundle_expired'/).result;
  expect(expiry.meta.changes).toBe(0);
  expect(expiry.meta.rows_read).toBeLessThan(10);
});
