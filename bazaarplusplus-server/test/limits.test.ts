import { env } from "cloudflare:test";
import { expect, test } from "vitest";

import migrationSql from "../migrations/0001_v5_initial.sql?raw";
import {
  CLAIM_MAX_LIMIT,
  D1_RETENTION_MS,
  DELIVERY_RETRY_BACKOFF_MS,
  MAX_BATTLES_PER_BUNDLE,
  MAX_DELIVERY_ATTEMPTS,
  R2_RETENTION_MS,
} from "../src/limits";

function compact(sql: string): string {
  return sql.replace(/\s+/g, " ");
}

function expectDeliveryLimits(sql: string): void {
  const source = compact(sql);
  expect(source).toContain(`delivery_attempts BETWEEN 0 AND ${MAX_DELIVERY_ATTEMPTS}`);
  expect(source).toContain(`attempt_number BETWEEN 1 AND ${MAX_DELIVERY_ATTEMPTS}`);
  expect(source).toContain(`delivery_attempts < ${MAX_DELIVERY_ATTEMPTS}`);
  expect(source).toContain(`delivery_attempts = ${MAX_DELIVERY_ATTEMPTS}`);
  expect(source).toContain(`active_claim_order BETWEEN 0 AND ${CLAIM_MAX_LIMIT - 1}`);
}

test("a V5 Bundle accepts at most 30 Battle projections", () => {
  expect(MAX_BATTLES_PER_BUNDLE).toBe(30);
});

test("R2 retention is eight days", () => {
  expect(R2_RETENTION_MS).toBe(8 * 86_400_000);
});

test("D1 retains Bundle metadata for fifteen days", () => {
  expect(D1_RETENTION_MS).toBe(15 * 86_400_000);
  expect(D1_RETENTION_MS).toBeGreaterThan(R2_RETENTION_MS);
});

test("delivery constants stay aligned with the migration and live schema", async () => {
  const schema = await env.DB.prepare(
    `SELECT sql FROM sqlite_master
     WHERE sql IS NOT NULL
       AND name IN (
         'bazaardb_deliveries',
         'bazaardb_delivery_attempts',
         'idx_bazaardb_claimable',
         'idx_bazaardb_exhausted_lease'
       )`,
  ).all<{ sql: string }>();

  expectDeliveryLimits(migrationSql);
  expectDeliveryLimits(schema.results.map(({ sql }) => sql).join("\n"));
  expect(DELIVERY_RETRY_BACKOFF_MS).toHaveLength(MAX_DELIVERY_ATTEMPTS - 1);
});
