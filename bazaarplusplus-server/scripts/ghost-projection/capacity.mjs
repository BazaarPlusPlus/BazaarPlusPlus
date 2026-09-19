import { writeFile } from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";
import { parseArgs } from "node:util";
import { cloudflare } from "./client.mjs";
import { legacyValues, summaryColumns } from "./operations.ts";

const { values } = parseArgs({
  options: {
    remote: { type: "boolean" },
    output: { type: "string" },
    "page-size": { type: "string", default: "10000" },
  },
});
if (!values.remote || !values.output)
  throw new Error("Use --remote --output PATH (read-only aggregate queries)");
const limit = Number(values["page-size"]);
if (!Number.isInteger(limit) || limit < 1 || limit > 20000)
  throw new Error("page-size must be 1..20000");
const textBytes = (c) => `COALESCE(length(CAST(${c} AS BLOB)), 0)`;
const intBytes = (c) =>
  `(CASE WHEN ${c} IS NULL OR ${c} IN (0,1) THEN 0 WHEN ${c} BETWEEN -128 AND 127 THEN 1 WHEN ${c} BETWEEN -32768 AND 32767 THEN 2 WHEN ${c} BETWEEN -8388608 AND 8388607 THEN 3 WHEN ${c} BETWEEN -2147483648 AND 2147483647 THEN 4 WHEN ${c} BETWEEN -140737488355328 AND 140737488355327 THEN 6 ELSE 8 END)`;
const ints = new Set(["recorded_at_ms", "is_final_battle", "day", "hour", "player_rating"]);
const payload = summaryColumns
  .map((c, i) => (ints.has(c) ? intBytes : textBytes)(legacyValues("g")[i]))
  .join(" + ");
const base = summaryColumns
  .slice(0, 6)
  .map((c) => (ints.has(c) ? intBytes : textBytes)(`g.${c}`))
  .join(" + ");
const indexes = `${[
  "opponent_account_id",
  "battle_id",
  "uploader_account_id",
  "bundle_id",
  "uploader_account_id",
  "battle_id",
]
  .map(textBytes)
  .join(" + ")} + ${intBytes("recorded_at_ms")}`;
const invalid = `json_type(projection_json, '$.day') IS NOT 'integer'
  OR json_type(projection_json, '$.hour') IS NOT 'integer'
  OR json_type(projection_json, '$.result') IS NOT 'text'
  OR json_type(projection_json, '$.player.display_name') IS NOT 'text'
  OR json_extract(projection_json, '$.player.account_id') IS NOT uploader_account_id
  OR json_extract(projection_json, '$.opponent.account_id') IS NOT opponent_account_id`;
const client = await cloudflare();
const initial = await client.metadata();
const report = {
  started_at: new Date().toISOString(),
  database: client.database,
  method:
    "Read-only keyset pages; UTF-8 and SQLite integer payload bytes, including both unchanged index payloads. Excludes record headers, B-tree/overflow pages and free space; pages are not a single snapshot.",
  physical_before_bytes: initial.file_size,
  page_size: limit,
  pages: 0,
  max_sql_ms: 0,
  rows: 0,
  old_fields_bytes: 0,
  summary_fields_bytes: 0,
  index_payload_bytes: 0,
  json_bytes: 0,
  invalid_required_rows: 0,
};
let cursor = ["", ""];
while (true) {
  const sql = `WITH page AS MATERIALIZED (
    SELECT * FROM ghost_battles WHERE (uploader_account_id, battle_id) > (?1, ?2)
    ORDER BY uploader_account_id, battle_id LIMIT ${limit}
  ) SELECT COUNT(*) AS rows,
    COALESCE(SUM(${base} + ${textBytes("projection_json")}), 0) AS old_fields_bytes,
    COALESCE(SUM(${payload}), 0) AS summary_fields_bytes,
    COALESCE(SUM(${indexes}), 0) AS index_payload_bytes,
    COALESCE(SUM(${textBytes("projection_json")}), 0) AS json_bytes,
    COALESCE(SUM(${invalid}), 0) AS invalid_required_rows,
    (SELECT uploader_account_id FROM page ORDER BY uploader_account_id DESC, battle_id DESC LIMIT 1) AS cursor_u,
    (SELECT battle_id FROM page ORDER BY uploader_account_id DESC, battle_id DESC LIMIT 1) AS cursor_b
    FROM page AS g`;
  const [result] = await client.query(sql, cursor);
  const row = result.results[0];
  report.pages++;
  report.max_sql_ms = Math.max(report.max_sql_ms, result.meta.duration);
  for (const key of [
    "rows",
    "old_fields_bytes",
    "summary_fields_bytes",
    "index_payload_bytes",
    "json_bytes",
    "invalid_required_rows",
  ])
    report[key] += row[key];
  if (report.pages % 10 === 0)
    console.log(
      JSON.stringify({ pages: report.pages, rows: report.rows, max_sql_ms: report.max_sql_ms }),
    );
  if (result.meta.duration > 2000)
    throw new Error("Read-only page exceeded 2s budget; rerun with a smaller page size");
  if (row.rows < limit) break;
  cursor = [row.cursor_u, row.cursor_b];
  await delay(200);
}
const final = await client.metadata();
Object.assign(report, {
  completed_at: new Date().toISOString(),
  physical_after_bytes: final.file_size,
  logical_saved_bytes: report.old_fields_bytes - report.summary_fields_bytes,
  old_ghost_with_indexes_bytes: report.old_fields_bytes + report.index_payload_bytes,
  summary_with_indexes_bytes: report.summary_fields_bytes + report.index_payload_bytes,
});
await writeFile(values.output, `${JSON.stringify(report, null, 2)}\n`);
console.log(JSON.stringify(report));
