import { resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { parseArgs } from "node:util";
import { readD1Migrations } from "@cloudflare/vitest-pool-workers";
import { Miniflare } from "miniflare";
import { cloudflare } from "./client.mjs";
import { cleanupStatements, pageStatements } from "./operations.ts";

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    remote: { type: "boolean", default: false },
    execute: { type: "boolean", default: false },
    "local-dir": { type: "string" },
    "page-size": { type: "string", default: "500" },
    "max-pages": { type: "string", default: "20" },
    "pause-ms": { type: "string", default: "250" },
    "worker-version": { type: "string" },
    concurrency: { type: "string", default: "1" },
    "skip-observation": { type: "boolean", default: false },
  },
});
const [command] = positionals;
const commands = [
  "init-local",
  "status",
  "prepare",
  "copy",
  "verify",
  "bridge",
  "retire",
  "cleanup",
  "finish",
];
if (positionals.length !== 1 || !commands.includes(command))
  throw new Error(`Command: ${commands.join(" | ")}`);
if (values.remote === Boolean(values["local-dir"]))
  throw new Error("Choose --remote or --local-dir PATH");
if (command !== "status" && !values.execute)
  throw new Error("Mutations require --execute after reviewing the runbook");
if (command === "init-local" && values.remote)
  throw new Error("init-local cannot target production");
const pageSize = Number(values["page-size"]);
const maxPages = Number(values["max-pages"]);
const pauseMs = Number(values["pause-ms"]);
const concurrency = Number(values.concurrency);
if (!Number.isInteger(concurrency) || concurrency < 1 || concurrency > 3)
  throw new Error("concurrency must be 1..3");
pageStatements("copy", pageSize);
if (!Number.isInteger(maxPages) || maxPages < 1 || maxPages > 10000)
  throw new Error("max-pages must be 1..10000");
if (!Number.isInteger(pauseMs) || pauseMs < 0 || pauseMs > 5000)
  throw new Error("pause-ms must be 0..5000");
let mf;
const cf = values.remote ? await cloudflare() : null;
if (!cf)
  mf = new Miniflare({
    modules: true,
    script: "",
    d1Databases: { DB: "ghost-summary-migration-local" },
    resourcePersistencePath: resolve(values["local-dir"]),
  });
const local = mf ? await mf.getD1Database("DB") : null;
const batch = async (sql) => {
  const result = cf
    ? await cf.query(sql.join(";\n"))
    : await local.batch(sql.map((s) => local.prepare(s)));
  const sqlMs = result.reduce((sum, r) => sum + Number(r.meta?.duration ?? 0), 0);
  if (sqlMs > 2000)
    throw new Error("Committed batch exceeded 2s latency budget; reduce page size before resuming");
  return result;
};
const read = async (sql) => (await batch([sql]))[0].results;
const migrations = await readD1Migrations(new URL("../../migrations", import.meta.url).pathname);
const apply = async (prefix, before = []) => {
  const migration = migrations.find((m) => m.name.startsWith(prefix));
  if (!migration) throw new Error(`Missing migration ${prefix}`);
  const found = await read(`SELECT name FROM d1_migrations WHERE name = '${migration.name}'`);
  if (found.length) return console.log(JSON.stringify({ already_applied: migration.name }));
  await batch([
    ...before,
    ...migration.queries,
    `INSERT INTO d1_migrations (name) VALUES ('${migration.name}')`,
  ]);
  console.log(JSON.stringify({ applied: migration.name }));
};
const status = async () => {
  const tables = await read(
    "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('ghost_projection_migration', 'ghost_battles', 'ghost_battle_summaries') ORDER BY name",
  );
  const state = tables.some((r) => r.name === "ghost_projection_migration")
    ? await read(
        "SELECT phase, legacy_duplicates, retire_authorized FROM ghost_projection_migration WHERE id = 1",
      )
    : [];
  const applied = await read("SELECT name FROM d1_migrations ORDER BY id");
  console.log(
    JSON.stringify({
      at: new Date().toISOString(),
      target: cf?.database ?? "local",
      tables,
      state,
      applied,
    }),
  );
};
try {
  if (command === "init-local") {
    await batch([
      "CREATE TABLE IF NOT EXISTS d1_migrations (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE NOT NULL, applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL)",
    ]);
    await apply("0001_");
    await apply("0002_");
  } else if (command === "prepare") {
    await apply("0003_");
  } else if (command === "bridge") {
    await apply("0004_");
  } else if (command === "retire") {
    if (cf) {
      const version = values["worker-version"];
      if (!version || !/^[0-9a-f-]{36}$/.test(version))
        throw new Error("retire requires --worker-version for the verified summary build");
      const deployment = await cf.deployments();
      const latest = [...deployment.deployments].sort((a, b) =>
        b.created_on.localeCompare(a.created_on),
      )[0];
      if (
        latest?.versions.length !== 1 ||
        latest.versions[0].version_id !== version ||
        latest.versions[0].percentage !== 100
      ) {
        throw new Error("Expected summary Worker must serve 100% of traffic");
      }
      if (
        !values["skip-observation"] &&
        Date.now() - Date.parse(latest.created_on) < 15 * 60 * 1000
      )
        throw new Error(
          "Wait at least 15 minutes after full deployment and confirm old requests drained",
        );
    }
    await apply("0005_", [
      "UPDATE ghost_projection_migration SET retire_authorized = 1 WHERE id = 1",
    ]);
  } else if (command === "finish") {
    await status(); // Keep the final legacy anomaly count in the operator log.
    await apply("0006_");
  } else if (["copy", "verify", "cleanup"].includes(command)) {
    const expected =
      command === "copy" ? "copying" : command === "verify" ? "verifying" : "retired";
    const terminal = command === "copy" ? "verifying" : "verified";
    const [initial] = await read("SELECT phase FROM ghost_projection_migration WHERE id = 1");
    let stopped = initial?.phase !== expected;
    let nextPage = 0;
    const lane = async () => {
      while (!stopped && nextPage < maxPages) {
        const page = ++nextPage;
        let result;
        try {
          result = await batch([
            ...(command === "cleanup"
              ? cleanupStatements(pageSize)
              : pageStatements(command, pageSize)),
            "SELECT phase FROM ghost_projection_migration WHERE id = 1",
          ]);
        } catch (error) {
          stopped = true;
          // Another in-flight page may have atomically completed this phase.
          // A stale phase guard aborts before touching data or advancing a cursor.
          if (command !== "cleanup") {
            const [current] = await read(
              "SELECT phase FROM ghost_projection_migration WHERE id = 1",
            );
            if (current?.phase === terminal) return;
          }
          throw error;
        }
        const rows =
          command === "cleanup" ? result[1].results.length : result[1].results[0].scanned;
        console.log(
          JSON.stringify({
            operation: command,
            page,
            rows,
            sql_ms: result.reduce((n, r) => n + Number(r.meta?.duration ?? 0), 0),
          }),
        );
        if (!rows || result.at(-1).results[0].phase !== expected) stopped = true;
        if (!stopped) await delay(pauseMs);
      }
    };
    const completed = await Promise.allSettled(Array.from({ length: concurrency }, lane));
    const failed = completed.find((r) => r.status === "rejected");
    if (failed) throw failed.reason;
  }
  await status();
} finally {
  await mf?.dispose();
}
