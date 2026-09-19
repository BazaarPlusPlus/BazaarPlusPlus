import { writeFile } from "node:fs/promises";
import { DatabaseSync } from "node:sqlite";
import { parseArgs } from "node:util";
import { readD1Migrations } from "@cloudflare/vitest-pool-workers";
import { cleanupStatements, pageStatements } from "./operations.ts";

const { values } = parseArgs({
  options: {
    database: { type: "string" },
    output: { type: "string" },
    rows: { type: "string", default: "1800000" },
  },
});
if (!values.database || !values.output)
  throw new Error("Use --database NEW_LOCAL_FILE --output REPORT.json");
const count = Number(values.rows);
if (!Number.isInteger(count) || count < 1) throw new Error("rows must be positive");
const db = new DatabaseSync(values.database);
if (db.prepare("SELECT 1 FROM sqlite_master LIMIT 1").get())
  throw new Error("Rehearsal requires an empty local database");
db.exec("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL");
const migrations = await readD1Migrations(new URL("../../migrations", import.meta.url).pathname);
const transaction = (sql) => {
  db.exec("BEGIN");
  try {
    for (const statement of sql) db.prepare(statement).all();
    db.exec("COMMIT");
  } catch (e) {
    db.exec("ROLLBACK");
    throw e;
  }
};
const apply = (prefix) => transaction(migrations.find((m) => m.name.startsWith(prefix)).queries);
apply("0001_");
apply("0002_");
const bundle =
  db.prepare(`INSERT INTO bundles (bundle_id,run_id,uploader_account_id,object_key,bundle_sha256,bundle_version,manifest_bytes,object_bytes,client_created_at_ms,stored_at_ms,available_at_ms,run_format_version,run_bytes,run_sha256,has_screenshot)
 VALUES (?1,?2,?3,?4,?5,5,1024,10000,1789200000000,1789200000000,1789200000000,5,8960,?5,0)`);
const ghost = db.prepare("INSERT INTO ghost_battles VALUES (?1,?2,?3,?4,1789200000000,1,?5)");
const started = performance.now();
for (let start = 0; start < count; start += 999) {
  db.exec("BEGIN");
  for (let i = start; i < Math.min(start + 999, count); i++) {
    const parent = Math.floor(i / 3);
    const bid = `01J8${String(parent).padStart(22, "0")}`;
    const uid = `u-${String(parent % 24000).padStart(34, "0")}`;
    const opponent = `o-${String(parent % 24000).padStart(34, "0")}`;
    const battle = `b-${String(i).padStart(34, "0")}`;
    if (i % 3 === 0)
      bundle.run(
        bid,
        `run-${String(parent).padStart(32, "0")}`,
        uid,
        `bundles/2026-09-12/${bid}.bundle`,
        "a".repeat(64),
      );
    const combatant = (account, name) => ({
      account_id: account,
      display_name: name,
      hero_id: null,
      hero_name: "Vanessa",
      rank: "Gold",
      rating: 1234,
      level: 10,
      prestige: 2,
      victories: 9,
    });
    ghost.run(
      uid,
      battle,
      bid,
      opponent,
      JSON.stringify({
        battle_id: battle,
        recorded_at_ms: 1789200000000,
        day: 10,
        hour: 18,
        encounter_id: null,
        combat_kind: "pvp",
        result: "loss",
        winner_combatant_id: "Opponent",
        loser_combatant_id: "Player",
        is_final_battle: true,
        player: combatant(uid, "Challenger"),
        opponent: combatant(opponent, "Local player"),
      }),
    );
  }
  db.exec("COMMIT");
  if (start % 99900 === 0) console.log(JSON.stringify({ seeded: Math.min(start + 999, count) }));
}
const footprint = () => {
  db.exec("PRAGMA wal_checkpoint(TRUNCATE)");
  const pageSize = db.prepare("PRAGMA page_size").get().page_size;
  return {
    file_bytes: pageSize * db.prepare("PRAGMA page_count").get().page_count,
    free_bytes: pageSize * db.prepare("PRAGMA freelist_count").get().freelist_count,
    ghost_allocated_bytes: db
      .prepare("SELECT COALESCE(SUM(pgsize),0) AS n FROM dbstat WHERE name LIKE '%ghost%'")
      .get().n,
  };
};
const report = {
  kind: "Synthetic local SQLite rehearsal, not production D1 timing or physical recovery",
  rows: count,
  seed_ms: performance.now() - started,
  before: footprint(),
  stages: {},
};
apply("0003_");
for (const [mode, phase] of [
  ["copy", "copying"],
  ["verify", "verifying"],
]) {
  const samples = [];
  while (db.prepare("SELECT phase FROM ghost_projection_migration").get().phase === phase) {
    const t = performance.now();
    transaction(pageStatements(mode, 500));
    samples.push(performance.now() - t);
  }
  samples.sort((a, b) => a - b);
  report.stages[mode] = {
    pages: samples.length,
    max_ms: samples.at(-1),
    p95_ms: samples[Math.floor(samples.length * 0.95)],
  };
  console.log(JSON.stringify({ mode, ...report.stages[mode] }));
}
report.peak = footprint();
apply("0004_");
db.exec("UPDATE ghost_projection_migration SET retire_authorized = 1");
apply("0005_");
const samples = [];
while (db.prepare("SELECT 1 FROM ghost_battles LIMIT 1").get()) {
  const t = performance.now();
  transaction(cleanupStatements(500));
  samples.push(performance.now() - t);
}
samples.sort((a, b) => a - b);
report.stages.cleanup = {
  pages: samples.length,
  max_ms: samples.at(-1),
  p95_ms: samples[Math.floor(samples.length * 0.95)],
};
apply("0006_");
report.after = footprint();
report.summary_rows = db.prepare("SELECT COUNT(*) AS n FROM ghost_battle_summaries").get().n;
report.foreign_key_errors = db.prepare("PRAGMA foreign_key_check").all().length;
report.completed_at = new Date().toISOString();
await writeFile(values.output, `${JSON.stringify(report, null, 2)}\n`);
console.log(JSON.stringify(report));
db.close();
