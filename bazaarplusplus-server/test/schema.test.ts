import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import { commitBundle } from "../src/modules/bundle-commit";
import { bundleData } from "./fixtures/bundle";

test("the completed migrations leave only the domain tables", async () => {
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
    "bundle_uploaders",
    "bundles",
    "ghost_battle_summaries",
  ]);
});

describe("relational constraints", () => {
  const validBundleSql = `INSERT INTO bundles (
    bundle_id, run_id, uploader_account_id, object_key, bundle_sha256,
    object_bytes, client_created_at_ms, stored_at_ms, available_at_ms, has_screenshot
  ) VALUES (?1, ?2, 'schema-uploader', ?3, ?4, 100, 1, 1, 1, ?5)`;

  test("foreign keys are enabled and reject a Ghost row without its Bundle", async () => {
    const foreignKeys = await env.DB.prepare(`PRAGMA foreign_keys`).first<{
      foreign_keys: number;
    }>();
    expect(foreignKeys?.foreign_keys).toBe(1);
    await expect(
      env.DB.prepare(
        `INSERT INTO ghost_battle_summaries (
          uploader_account_id, battle_id, bundle_id, opponent_account_id,
          recorded_at_ms, day, hour, result, winner_combatant_id, player_display_name
        ) VALUES ('a', 'b', 'missing', 'c', 1, 1, 1, 'win', 'Player', 'Uploader')`,
      ).run(),
    ).rejects.toThrow();
  });

  test.each([null, 2])("has_screenshot rejects %s", async (hasScreenshot) => {
    await expect(
      env.DB.prepare(validBundleSql)
        .bind(
          "01J00000000000000000000501",
          "schema-run-screenshot-flag",
          "bundles/2026-08-02/01J00000000000000000000501.bundle",
          "a".repeat(64),
          hasScreenshot,
        )
        .run(),
    ).rejects.toThrow();
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
