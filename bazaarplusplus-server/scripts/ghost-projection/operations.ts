// These SQL statements run as one D1 batch per page. The cursor commits with the data.
const columns = [
  "uploader_account_id",
  "battle_id",
  "bundle_id",
  "opponent_account_id",
  "recorded_at_ms",
  "is_final_battle",
  "day",
  "hour",
  "result",
  "winner_combatant_id",
  "player_display_name",
  "player_hero_name",
  "opponent_hero_name",
  "player_rank",
  "player_rating",
];
const paths = [
  "day",
  "hour",
  "result",
  "winner_combatant_id",
  "player.display_name",
  "player.hero_name",
  "opponent.hero_name",
  "player.rank",
  "player.rating",
];
export const legacyValues = (alias: string): string[] => [
  ...columns.slice(0, 6).map((column) => `${alias}.${column}`),
  ...paths.map((path) => `json_extract(${alias}.projection_json, '$.${path}')`),
];
export const summaryColumns = columns;

export function pageStatements(mode: "copy" | "verify", limit: number): string[] {
  if (!Number.isInteger(limit) || limit < 1 || limit > 1000)
    throw new Error("Page size must be 1..1000");
  const phase = mode === "copy" ? "copying" : "verifying";
  const page = `SELECT * FROM ghost_battles
    WHERE (uploader_account_id, battle_id) > (
      SELECT cursor_uploader, cursor_battle FROM ghost_projection_migration WHERE id = 1
    ) ORDER BY uploader_account_id, battle_id LIMIT ${limit}`;
  const differences = columns
    .map((column, index) => `s.${column} IS NOT ${legacyValues("g")[index]}`)
    .join(" OR ");
  return [
    `UPDATE ghost_projection_migration SET phase = CASE WHEN phase = '${phase}' THEN phase ELSE 'invalid' END WHERE id = 1`,
    `SELECT COUNT(*) AS scanned FROM (${page})`,
    mode === "copy"
      ? `INSERT INTO ghost_battle_summaries (${columns.join(", ")})
         SELECT ${legacyValues("g").join(", ")} FROM (${page}) AS g WHERE 1
         ON CONFLICT(uploader_account_id, battle_id) DO NOTHING`
      : `UPDATE ghost_projection_migration SET phase = CASE WHEN EXISTS (
           SELECT 1 FROM (${page}) AS g LEFT JOIN ghost_battle_summaries AS s
             ON s.uploader_account_id = g.uploader_account_id AND s.battle_id = g.battle_id
           WHERE ${differences}
         ) THEN 'invalid' ELSE phase END WHERE id = 1`,
    `WITH page AS MATERIALIZED (${page})
     UPDATE ghost_projection_migration SET
       cursor_uploader = COALESCE((SELECT uploader_account_id FROM page ORDER BY uploader_account_id DESC, battle_id DESC LIMIT 1), ''),
       cursor_battle = COALESCE((SELECT battle_id FROM page ORDER BY uploader_account_id DESC, battle_id DESC LIMIT 1), ''),
       phase = CASE WHEN NOT EXISTS (SELECT 1 FROM page) THEN '${mode === "copy" ? "verifying" : "verified"}' ELSE phase END
     WHERE id = 1`,
  ];
}

export function cleanupStatements(limit: number): string[] {
  if (!Number.isInteger(limit) || limit < 1 || limit > 1000)
    throw new Error("Page size must be 1..1000");
  return [
    "UPDATE ghost_projection_migration SET phase = CASE WHEN phase = 'retired' THEN phase ELSE 'invalid' END WHERE id = 1",
    `DELETE FROM ghost_battles WHERE (uploader_account_id, battle_id) IN (
      SELECT uploader_account_id, battle_id FROM ghost_battles ORDER BY uploader_account_id, battle_id LIMIT ${limit}
    ) RETURNING 1 AS deleted`,
  ];
}
