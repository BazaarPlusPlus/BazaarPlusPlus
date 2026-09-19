import { env } from "cloudflare:test";
import { beforeEach, expect, test, vi } from "vitest";
import { cleanupStatements, pageStatements } from "../scripts/ghost-projection/operations";
import { commitBundle } from "../src/modules/bundle-commit";
import { pruneExpiredBundles } from "../src/modules/d1-retention";
import { discoverGhostBattles } from "../src/modules/ghost-battle-discovery";
import { bundleData } from "./fixtures/bundle";
import { createTestDeps } from "./fixtures/deps";

const NOW = 1_789_200_000_000;
const times = { storedAtMs: NOW, availableAtMs: NOW };
const batch = (queries: string[]) => env.DB.batch(queries.map((sql) => env.DB.prepare(sql)));
const migration = async (prefix: string) => {
  const found = env.TEST_MIGRATIONS.find((m) => m.name.startsWith(prefix));
  if (!found) throw new Error("Missing migration");
  return batch(found.queries);
};
const state = () => env.DB.prepare("SELECT * FROM ghost_projection_migration WHERE id = 1").first();
const copyAndVerify = async (size = 1) => {
  for (const [mode, phase] of [
    ["copy", "copying"],
    ["verify", "verifying"],
  ] as const) {
    for (let i = 0; i < 100; i++) {
      if ((await state())?.phase !== phase) break;
      await batch(pageStatements(mode, size));
    }
  }
  expect((await state())?.phase).toBe("verified");
};

// Exact INSERT used by the deployed JSON writer; all other Bundle statements
// still go through the public commit interface, including the final uploader write.
const legacyInsert = `INSERT INTO ghost_battles (
  uploader_account_id, battle_id, bundle_id, opponent_account_id,
  recorded_at_ms, is_final_battle, projection_json
) SELECT ?2, json_extract(value, '$.battle_id'), ?3,
  json_extract(value, '$.opponent.account_id'), json_extract(value, '$.recorded_at_ms'),
  CASE json_extract(value, '$.is_final_battle') WHEN 1 THEN 1 ELSE 0 END, value
FROM json_each(?1)
WHERE json_extract(value, '$.opponent.account_id') = ?2 OR EXISTS (
  SELECT 1 FROM bundle_uploaders WHERE player_account_id = json_extract(value, '$.opponent.account_id')
) ON CONFLICT(uploader_account_id, battle_id) DO NOTHING`;
const legacyDb = new Proxy(env.DB, {
  get(target, prop) {
    if (prop === "prepare")
      return (sql: string) =>
        target.prepare(sql.includes("INSERT INTO ghost_battle_summaries") ? legacyInsert : sql);
    const value = Reflect.get(target, prop);
    return typeof value === "function" ? value.bind(target) : value;
  },
});
const data = (id: number, uploader = "migrate-uploader") =>
  bundleData({
    bundleId: `01J8${String(id).padStart(22, "0")}`,
    runId: `migrate-run-${id}`,
    uploaderAccountId: uploader,
    createdAtMs: NOW,
  });
const upload = async (id: number, legacy: boolean, uploader?: string) => {
  const fixture = await data(id, uploader);
  await commitBundle(legacy ? legacyDb : env.DB, fixture.descriptor, fixture.digest, times, {
    projectionDuplicate() {},
  });
  return fixture;
};

beforeEach(async () => {
  const triggers = await env.DB.prepare(
    "SELECT name FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'ghost_%'",
  ).all<{ name: string }>();
  if (triggers.results.length)
    await batch(triggers.results.map(({ name }) => `DROP TRIGGER ${name}`));
  for (const table of [
    "ghost_battle_summaries",
    "ghost_battles",
    "ghost_projection_migration",
    "ghost_migration_gate",
  ]) {
    await env.DB.prepare(`DROP TABLE IF EXISTS ${table}`).run();
  }
  await env.DB.prepare("DELETE FROM bundles").run();
  await env.DB.prepare("DELETE FROM bundle_uploaders").run();
  await batch(
    env.TEST_MIGRATIONS[0].queries.filter((sql) =>
      /CREATE (TABLE ghost_battles|INDEX idx_ghost_battles_query)/.test(sql),
    ),
  );
  await env.DB.prepare("CREATE INDEX idx_ghost_battles_bundle ON ghost_battles(bundle_id)").run();
});

test("backfill resumes atomically, mirrors arrivals behind the cursor, and respects retention", async () => {
  await upload(1, true, "m-uploader");
  const expired = await upload(2, true, "z-uploader");
  await migration("0003_");
  await expect(migration("0004_")).rejects.toThrow();
  expect((await state())?.phase).toBe("copying");
  await expect(
    batch([...pageStatements("copy", 1), "SELECT * FROM missing_table"]),
  ).rejects.toThrow();
  expect((await state())?.cursor_uploader).toBe("");
  expect(await env.DB.prepare("SELECT COUNT(*) AS n FROM ghost_battle_summaries").first()).toEqual({
    n: 0,
  });
  await batch(pageStatements("copy", 1));
  await upload(3, true, "a-uploader");
  await env.DB.prepare("UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2")
    .bind(NOW - 15 * 86400000 - 1, expired.descriptor.bundleId)
    .run();
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({ deleted: 1, hasMore: false });
  await copyAndVerify();
  await migration("0004_");
  expect(
    await env.DB.prepare(
      "SELECT uploader_account_id FROM ghost_battle_summaries ORDER BY uploader_account_id",
    ).all(),
  ).toMatchObject({
    results: [{ uploader_account_id: "a-uploader" }, { uploader_account_id: "m-uploader" }],
  });
  expect(
    await env.DB.prepare(
      "SELECT player_account_id FROM bundle_uploaders WHERE player_account_id = 'z-uploader'",
    ).first(),
  ).not.toBeNull();
});

test("verification refuses mismatched retained fields without advancing its cursor", async () => {
  await upload(4, true);
  await migration("0003_");
  await batch(pageStatements("copy", 1));
  await batch(pageStatements("copy", 1));
  await env.DB.prepare("UPDATE ghost_battle_summaries SET player_rating = 999").run();
  await expect(batch(pageStatements("verify", 1))).rejects.toThrow();
  expect((await state())?.phase).toBe("verifying");
  expect((await state())?.cursor_uploader).toBe("");
  await expect(migration("0004_")).rejects.toThrow();
});

test("both writer orders preserve first projections and report mixed duplicates during overlap", async () => {
  await migration("0003_");
  await copyAndVerify();
  await migration("0004_");
  const oldFirst = await upload(10, true, "old-first");
  const fresh = await data(11, "old-first");
  fresh.descriptor.battles[0].player.display_name = "Must not replace first";
  fresh.descriptor.battles.push({ ...fresh.descriptor.battles[0], battle_id: "another-battle" });
  const observer = { projectionDuplicate: vi.fn() };
  expect(await commitBundle(env.DB, fresh.descriptor, fresh.digest, times, observer)).toEqual({
    kind: "committed",
    projection: { eligible: 2, inserted: 1 },
  });
  expect(observer.projectionDuplicate).toHaveBeenCalledWith({
    bundle_id: fresh.descriptor.bundleId,
    dropped: 1,
  });
  const newFirst = await upload(12, false, "new-first");
  await upload(13, true, "new-first");
  expect((await state())?.legacy_duplicates).toBe(1);
  for (const fixture of [oldFirst, newFirst]) {
    for (const table of ["ghost_battles", "ghost_battle_summaries"]) {
      expect(
        await env.DB.prepare(
          `SELECT bundle_id FROM ${table} WHERE uploader_account_id = ?1 AND battle_id = 'battle-001'`,
        )
          .bind(fixture.descriptor.uploaderAccountId)
          .first(),
      ).toEqual({ bundle_id: fixture.descriptor.bundleId });
    }
  }
  // A lost successful response retries as a Bundle duplicate, without another projection.
  expect((await commitBundle(env.DB, fresh.descriptor, fresh.digest, times)).kind).toBe(
    "duplicate",
  );
  const result = await discoverGhostBattles(
    new Request("https://worker.test/ghost-battles?player_account_id=old-first"),
    env,
    "migration",
    createTestDeps({ now: () => NOW }),
  );
  expect(result.battles).toMatchObject([
    { player: { display_name: "Uploader", rank: "Gold", rating: 1234 } },
    { player: { display_name: "Must not replace first" } },
  ]);
});

test("retirement is gated, rejects late legacy commits, and removes JSON only after bounded draining", async () => {
  await upload(20, true);
  await migration("0003_");
  await copyAndVerify();
  await migration("0004_");
  await expect(migration("0005_")).rejects.toThrow();
  await env.DB.prepare("UPDATE ghost_projection_migration SET retire_authorized = 1").run();
  await migration("0005_");
  await expect(upload(21, true, "late-uploader")).rejects.toMatchObject({ status: 503 });
  expect(
    await env.DB.prepare(
      "SELECT player_account_id FROM bundle_uploaders WHERE player_account_id = 'late-uploader'",
    ).first(),
  ).toBeNull();
  const newRow = await upload(22, false, "late-uploader");
  await expect(migration("0006_")).rejects.toThrow();
  const cleanup = await batch(cleanupStatements(1));
  expect(cleanup[1].results).toHaveLength(1);
  expect((await batch(cleanupStatements(1)))[1].results).toHaveLength(0);
  await migration("0006_");
  expect(
    await env.DB.prepare("SELECT name FROM sqlite_master WHERE name = 'ghost_battles'").first(),
  ).toBeNull();
  expect(
    (await env.DB.prepare("PRAGMA table_info(ghost_battle_summaries)").all()).results,
  ).toHaveLength(15);
  expect(await env.DB.prepare("SELECT COUNT(*) AS n FROM ghost_battle_summaries").first()).toEqual({
    n: 2,
  });
  await env.DB.prepare("DELETE FROM bundles WHERE bundle_id = ?1")
    .bind(newRow.descriptor.bundleId)
    .run();
  expect(await env.DB.prepare("SELECT COUNT(*) AS n FROM ghost_battle_summaries").first()).toEqual({
    n: 1,
  });
});

test.each([0, 1])("concurrent writers converge with recursive_triggers=%i", async (recursive) => {
  await migration("0003_");
  await copyAndVerify();
  await migration("0004_");
  await env.DB.prepare(`PRAGMA recursive_triggers = ${recursive}`).run();
  const older = await data(30, "concurrent-uploader");
  const newer = await data(31, "concurrent-uploader");
  const observer = { projectionDuplicate: vi.fn() };
  await Promise.all([
    commitBundle(legacyDb, older.descriptor, older.digest, times, { projectionDuplicate() {} }),
    commitBundle(env.DB, newer.descriptor, newer.digest, times, observer),
  ]);
  const legacy = await env.DB.prepare("SELECT bundle_id FROM ghost_battles").all();
  const summaries = await env.DB.prepare("SELECT bundle_id FROM ghost_battle_summaries").all();
  expect(summaries.results).toEqual(legacy.results);
  expect(summaries.results).toHaveLength(1);
  const metric =
    Number((await state())?.legacy_duplicates) +
    observer.projectionDuplicate.mock.calls.reduce((n, [entry]) => n + entry.dropped, 0);
  expect(metric).toBe(1);
});

test("missing migration state cannot bypass the bridge gate", async () => {
  await migration("0003_");
  await env.DB.prepare("DELETE FROM ghost_projection_migration").run();
  await expect(migration("0004_")).rejects.toThrow();
  expect(
    await env.DB.prepare(
      "SELECT name FROM sqlite_master WHERE name = 'ghost_summary_copy_guard'",
    ).first(),
  ).not.toBeNull();
});

test("overlapping copy pages commit distinct cursor ranges and reject a stale completion page", async () => {
  await upload(40, true, "a-page-uploader");
  await upload(41, true, "b-page-uploader");
  await upload(42, true, "c-page-uploader");
  await migration("0003_");
  const copied = await Promise.all([
    batch(pageStatements("copy", 1)),
    batch(pageStatements("copy", 1)),
  ]);
  expect(copied.map((r) => (r[1].results[0] as { scanned: number }).scanned)).toEqual([1, 1]);
  expect(await env.DB.prepare("SELECT COUNT(*) AS n FROM ghost_battle_summaries").first()).toEqual({
    n: 2,
  });
  await batch(pageStatements("copy", 1));
  const tail = await Promise.allSettled([
    batch(pageStatements("copy", 1)),
    batch(pageStatements("copy", 1)),
  ]);
  expect(tail.filter((r) => r.status === "fulfilled")).toHaveLength(1);
  expect(tail.filter((r) => r.status === "rejected")).toHaveLength(1);
  expect((await state())?.phase).toBe("verifying");
  expect(await env.DB.prepare("SELECT COUNT(*) AS n FROM ghost_battle_summaries").first()).toEqual({
    n: 3,
  });
});
