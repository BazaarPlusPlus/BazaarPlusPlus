import { env } from "cloudflare:test";
import { getTableConfig } from "drizzle-orm/sqlite-core";
import { describe, expect, test } from "vitest";

import { bundleIdentity } from "../src/db-schema";
import { commitBundle } from "../src/modules/bundle-commit";
import { bundleData } from "./fixtures/bundle";

test("the completed migrations leave only the V5 domain tables", async () => {
  const result = await env.DB.prepare(
    `
      SELECT name
      FROM sqlite_master
      WHERE type = 'table'
        AND name NOT LIKE '_cf_%'
        AND name NOT LIKE 'sqlite_%'
        AND name <> 'd1_migrations'
      ORDER BY name
    `,
  ).all<{ name: string }>();

  expect(result.results.map(({ name }) => name)).toEqual([
    "bazaardb_deliveries",
    "bazaardb_delivery_attempts",
    "bundle_uploaders",
    "bundles",
    "ghost_battle_summaries",
  ]);
});

test("the Drizzle identity projection matches the migrated Bundle columns", async () => {
  const table = getTableConfig(bundleIdentity);
  const result = await env.DB.prepare("PRAGMA table_info(bundles)").all<{
    name: string;
    type: string;
    notnull: number;
  }>();
  const columns = new Map(result.results.map((column) => [column.name, column]));

  expect(table.name).toBe("bundles");
  for (const column of table.columns) {
    expect(columns.get(column.name)).toMatchObject({
      name: column.name,
      type: column.getSQLType().toUpperCase(),
      notnull: Number(column.notNull),
    });
  }
});

describe("V5 relational constraints", () => {
  const validBundleSql = `INSERT INTO bundles (
    bundle_id, run_id, uploader_account_id, object_key, bundle_sha256,
    bundle_version, manifest_bytes, object_bytes, client_created_at_ms,
    stored_at_ms, available_at_ms, run_format_version, run_bytes, run_sha256,
    has_screenshot, screenshot_content_type, screenshot_bytes, screenshot_sha256
  ) VALUES (?1, ?2, 'schema-uploader', ?3, ?4, 5, 10, 100, 1, 1, 1, 5, 10, ?4, ?5, ?6, ?7, ?8)`;

  test("foreign keys are enabled and reject a Ghost row without its Bundle", async () => {
    const foreignKeys = await env.DB.prepare(`PRAGMA foreign_keys`).first<{
      foreign_keys: number;
    }>();
    expect(foreignKeys?.foreign_keys).toBe(1);
    await expect(
      env.DB.prepare(
        `INSERT INTO ghost_battle_summaries (
          uploader_account_id, battle_id, bundle_id, opponent_account_id,
          recorded_at_ms, day, hour, result, player_display_name
        ) VALUES ('a', 'b', 'missing', 'c', 1, 1, 1, 'win', 'Uploader')`,
      ).run(),
    ).rejects.toThrow();
  });

  test("Screenshot metadata cannot exploit SQLite NULL CHECK semantics", async () => {
    await expect(
      env.DB.prepare(validBundleSql)
        .bind(
          "01J00000000000000000000501",
          "schema-run-screenshot-null",
          "bundles/2026-08-02/01J00000000000000000000501.bundle",
          "a".repeat(64),
          1,
          null,
          null,
          null,
        )
        .run(),
    ).rejects.toThrow();
  });

  test("active claim order must be an integer within the claim page", async () => {
    const bundleId = "01J00000000000000000000503";
    await env.DB.prepare(validBundleSql)
      .bind(
        bundleId,
        "schema-run-fractional-claim-order",
        `bundles/2026-08-02/${bundleId}.bundle`,
        "c".repeat(64),
        0,
        null,
        null,
        null,
      )
      .run();

    await expect(
      env.DB.prepare(
        `INSERT INTO bazaardb_deliveries (
          bundle_id, active_claim_id, active_claim_order, claimable_at_ms,
          delivery_attempts, created_at_ms, state_updated_at_ms
        ) VALUES (?1, 'claim-fractional', 0.5, 2, 1, 1, 1)`,
      )
        .bind(bundleId)
        .run(),
    ).rejects.toThrow();
  });

  test("a failed D1 batch rolls back the entire Bundle logical commit", async () => {
    const bundleId = "01J00000000000000000000502";
    await expect(
      env.DB.batch([
        env.DB.prepare(validBundleSql).bind(
          bundleId,
          "schema-run-atomic",
          `bundles/2026-08-02/${bundleId}.bundle`,
          "b".repeat(64),
          0,
          null,
          null,
          null,
        ),
        env.DB.prepare(
          `INSERT INTO ghost_battle_summaries (
            uploader_account_id, battle_id, bundle_id, opponent_account_id,
            recorded_at_ms, day, hour, result, player_display_name
          ) VALUES ('schema-uploader', 'missing-day', ?1, 'schema-uploader', 1, NULL, 1, 'win', 'Uploader')`,
        ).bind(bundleId),
        env.DB.prepare(
          `INSERT INTO bundle_uploaders (player_account_id, first_bundle_at_ms)
           VALUES ('schema-uploader', 1)`,
        ),
      ]),
    ).rejects.toThrow();
    expect(
      await env.DB.prepare(`SELECT bundle_id FROM bundles WHERE bundle_id = ?1`)
        .bind(bundleId)
        .first(),
    ).toBeNull();
    expect(
      await env.DB.prepare(
        `SELECT player_account_id FROM bundle_uploaders WHERE player_account_id = 'schema-uploader'`,
      ).first(),
    ).toBeNull();
  });

  test("commitBundle exposes D1 batch rollback through its public interface", async () => {
    const times = { availableAtMs: 20, storedAtMs: 19 };
    const seed = await bundleData({
      bundleId: "01J00000000000000000000504",
      runId: "schema-commit-seed-run",
      uploaderAccountId: "schema-commit-seed-uploader",
      battles: [],
    });
    const target = await bundleData({
      bundleId: "01J00000000000000000000505",
      runId: "schema-commit-target-run",
      uploaderAccountId: "schema-commit-target-uploader",
      battles: [],
    });
    await commitBundle(env.DB, seed.descriptor, seed.digest, times);
    const collidingDescriptor = {
      ...target.descriptor,
      objectKey: seed.descriptor.objectKey,
    };

    await expect(
      commitBundle(env.DB, collidingDescriptor, target.digest, times),
    ).rejects.toMatchObject({
      status: 503,
      code: "storage_unavailable",
      message: "Bundle index commit failed",
    });
    expect(
      await env.DB.prepare(`SELECT bundle_id FROM bundles WHERE bundle_id = ?1`)
        .bind(target.descriptor.bundleId)
        .first(),
    ).toBeNull();
    expect(
      await env.DB.prepare(
        `SELECT player_account_id FROM bundle_uploaders WHERE player_account_id = ?1`,
      )
        .bind(target.descriptor.uploaderAccountId)
        .first(),
    ).toBeNull();
  });
});
