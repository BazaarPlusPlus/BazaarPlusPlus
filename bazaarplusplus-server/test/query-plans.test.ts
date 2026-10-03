import { env } from "cloudflare:test";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import worker from "../src/index";
import { seedDeliveryBacklog } from "./fixtures/backlog";
import { type RecordedD1Query, recordD1 } from "./fixtures/d1";

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
  return new Request(url, { headers: { Authorization: `Bearer ${env.BUNDLE_SYNC_TOKEN}` } });
}

function deliveryRequest(path: string, body: unknown): Request {
  return new Request(`https://worker.test/bazaardb/deliveries/${path}`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${env.BAZAARDB_DELIVERY_TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });
}

beforeEach(() => {
  vi.spyOn(Date, "now").mockReturnValue(NOW);
});
afterEach(() => vi.restoreAllMocks());

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

test("keyset, Ghost, and empty expiry reads stay bounded with a large live backlog", async () => {
  await seedDeliveryBacklog(env.DB, 2000, AVAILABLE, NOW + 60_000);

  const queries = recordD1(env.DB);
  const response = await worker.fetch(
    collectionRequest(`01J9${String(1948).padStart(22, "0")}`),
    env,
  );
  const page = (await response.json()) as {
    items: unknown[];
    next_after: { available_at_ms: number; bundle_id: string } | null;
  };
  expect(page.items).toHaveLength(50);
  expect(page.next_after).toEqual({
    available_at_ms: AVAILABLE,
    bundle_id: `01J9${String(1998).padStart(22, "0")}`,
  });
  const collection = query(queries, /^SELECT .* FROM bundles /).result;
  expect(collection.results).toHaveLength(51);
  expect(collection.meta.rows_read).toBeLessThan(100);

  // One opponent owns 20 of 2,000 Ghost rows; its page joins only those Bundles.
  await env.DB.prepare(`
    INSERT INTO ghost_battle_summaries (
      uploader_account_id, battle_id, bundle_id, opponent_account_id,
      recorded_at_ms, is_final_battle, day, hour, result, player_display_name
    )
    SELECT 'backlog-uploader', 'battle-' || bundle_id, bundle_id,
      CASE WHEN bundle_id < '01J9' || printf('%022d', 20) THEN 'plan-opponent' ELSE 'bystander' END,
      available_at_ms, 0, 1, 1, 'win', 'Uploader'
    FROM bundles WHERE uploader_account_id = 'backlog-uploader'
  `).run();
  const ghosts = await worker.fetch(
    new Request("https://worker.test/ghost-battles?player_account_id=plan-opponent"),
    { ...env, GHOST_BATTLE_RATE_LIMITER: { limit: async () => ({ success: true }) } },
  );
  expect(((await ghosts.json()) as { battles: unknown[] }).battles).toHaveLength(20);
  const ghost = query(queries, /^SELECT .* FROM ghost_battle_summaries /).result;
  expect(ghost.meta.rows_read).toBeLessThan(20 * 3 + 10);

  const claim = await worker.fetch(deliveryRequest("claim", {}), env);
  expect(await claim.json()).toEqual({ claim_id: null, expires_at_ms: null, items: [] });
  const expiry = query(queries, /^UPDATE .*failure_reason = 'bundle_expired'/).result;
  expect(expiry.meta.changes).toBe(0);
  expect(expiry.meta.rows_read).toBeLessThan(10);
});
