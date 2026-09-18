import { eq } from "drizzle-orm";
import { drizzle } from "drizzle-orm/d1";

import type { ValidatedBundleDescriptor } from "../bundle/manifest";
import { bundleIdentity } from "../db-schema";
import { HttpError } from "../errors";
import { logEvent } from "../observability";

export interface BundleReceipt {
  bundle_id: string;
  run_id: string;
  outcome: "stored" | "duplicate";
  bazaardb_delivery: "created" | "existing" | "not_applicable";
}

export type CommitOutcome =
  | { kind: "committed"; projection: { eligible: number; inserted: number } }
  | { kind: "duplicate"; receipt: BundleReceipt }
  | { kind: "conflict"; reason: "bundle_id_conflict" | "run_already_bundled" };

export interface CommitTimes {
  availableAtMs: number;
  storedAtMs: number;
}

export interface CommitObserver {
  projectionDuplicate(fields: { bundle_id: string; dropped: number }): void;
}

type ExistingBundleRow = typeof bundleIdentity.$inferSelect;

type StatementName =
  | "insert_bundle"
  | "insert_ghost"
  | "insert_delivery"
  | "count_eligible"
  | "insert_uploader";

interface NamedStatement {
  name: StatementName;
  statement: D1PreparedStatement;
  read?: "inserted" | "eligible";
}

const ELIGIBILITY_PREDICATE = `
  json_extract(value, '$.opponent.account_id') = ?2
  OR EXISTS (
    SELECT 1 FROM bundle_uploaders
    WHERE player_account_id = json_extract(value, '$.opponent.account_id')
  )`;

const DEFAULT_OBSERVER: CommitObserver = {
  projectionDuplicate(fields) {
    logEvent("bundle.projection.duplicate", fields);
  },
};

function duplicateReceipt(row: ExistingBundleRow): BundleReceipt {
  // The ingest receipt intentionally derives delivery existence from the immutable
  // Screenshot bit rather than consulting the delivery projection table.
  return {
    bundle_id: row.bundle_id,
    run_id: row.run_id,
    outcome: "duplicate",
    bazaardb_delivery: row.has_screenshot === 1 ? "existing" : "not_applicable",
  };
}

function decideFromExisting(
  descriptor: ValidatedBundleDescriptor,
  digest: string,
  bundle: ExistingBundleRow | null,
  runBundleId: string | null,
): CommitOutcome | null {
  if (bundle !== null) {
    return bundle.bundle_sha256 === digest
      ? { kind: "duplicate", receipt: duplicateReceipt(bundle) }
      : { kind: "conflict", reason: "bundle_id_conflict" };
  }
  if (runBundleId !== null && runBundleId !== descriptor.bundleId) {
    return { kind: "conflict", reason: "run_already_bundled" };
  }
  return null;
}

export async function inspectExistingBundle(
  db: D1Database,
  descriptor: ValidatedBundleDescriptor,
  digest: string,
): Promise<CommitOutcome | null> {
  const queries = drizzle(db);
  const [bundle, run] = await Promise.all([
    queries
      .select()
      .from(bundleIdentity)
      .where(eq(bundleIdentity.bundle_id, descriptor.bundleId))
      .get(),
    queries
      .select({ bundle_id: bundleIdentity.bundle_id })
      .from(bundleIdentity)
      .where(eq(bundleIdentity.run_id, descriptor.runId))
      .get(),
  ]);
  return decideFromExisting(descriptor, digest, bundle ?? null, run?.bundle_id ?? null);
}

function buildStatements(
  db: D1Database,
  descriptor: ValidatedBundleDescriptor,
  digest: string,
  times: CommitTimes,
): NamedStatement[] {
  const screenshot = descriptor.screenshot;
  const battles = JSON.stringify(descriptor.battles);
  const statements: NamedStatement[] = [
    {
      name: "insert_bundle",
      statement: db
        .prepare(
          `INSERT INTO bundles (
            bundle_id, run_id, uploader_account_id, object_key, bundle_sha256,
            bundle_version, manifest_bytes, object_bytes, client_created_at_ms,
            stored_at_ms, available_at_ms, run_format_version, run_bytes, run_sha256,
            has_screenshot, screenshot_content_type, screenshot_bytes, screenshot_sha256
          ) VALUES (?1, ?2, ?3, ?4, ?5, 5, ?6, ?7, ?8, ?9, ?10, 5, ?11, ?12, ?13, ?14, ?15, ?16)`,
        )
        .bind(
          descriptor.bundleId,
          descriptor.runId,
          descriptor.uploaderAccountId,
          descriptor.objectKey,
          digest,
          descriptor.manifestBytes,
          descriptor.objectBytes,
          descriptor.createdAtMs,
          times.storedAtMs,
          times.availableAtMs,
          descriptor.run.length,
          descriptor.run.sha256,
          screenshot === null ? 0 : 1,
          screenshot?.contentType ?? null,
          screenshot?.length ?? null,
          screenshot?.sha256 ?? null,
        ),
    },
    {
      name: "insert_ghost",
      read: "inserted",
      statement: db
        .prepare(
          `INSERT INTO ghost_battle_summaries (
            uploader_account_id, battle_id, bundle_id, opponent_account_id,
            recorded_at_ms, is_final_battle, day, hour, result, winner_combatant_id,
            player_display_name, player_hero_name, opponent_hero_name, player_rank, player_rating
          )
          SELECT ?2,
                 json_extract(value, '$.battle_id'),
                 ?3,
                 json_extract(value, '$.opponent.account_id'),
                 json_extract(value, '$.recorded_at_ms'),
                 CASE json_extract(value, '$.is_final_battle') WHEN 1 THEN 1 ELSE 0 END,
                 json_extract(value, '$.day'),
                 json_extract(value, '$.hour'),
                 json_extract(value, '$.result'),
                 json_extract(value, '$.winner_combatant_id'),
                 json_extract(value, '$.player.display_name'),
                 json_extract(value, '$.player.hero_name'),
                 json_extract(value, '$.opponent.hero_name'),
                 json_extract(value, '$.player.rank'),
                 json_extract(value, '$.player.rating')
          FROM json_each(?1)
          WHERE ${ELIGIBILITY_PREDICATE}
          ON CONFLICT(uploader_account_id, battle_id) DO NOTHING
          RETURNING battle_id`,
        )
        .bind(battles, descriptor.uploaderAccountId, descriptor.bundleId),
    },
    {
      name: "insert_delivery",
      statement: db
        .prepare(
          `INSERT INTO bazaardb_deliveries (
            bundle_id, claimable_at_ms, created_at_ms, state_updated_at_ms
          )
          SELECT ?1, ?2, ?2, ?2 WHERE ?3 = 1`,
        )
        .bind(descriptor.bundleId, times.availableAtMs, screenshot === null ? 0 : 1),
    },
    {
      name: "count_eligible",
      read: "eligible",
      statement: db
        .prepare(
          `SELECT COUNT(*) AS eligible
           FROM json_each(?1)
           WHERE ${ELIGIBILITY_PREDICATE}`,
        )
        .bind(battles, descriptor.uploaderAccountId),
    },
    {
      name: "insert_uploader",
      statement: db
        .prepare(
          `INSERT INTO bundle_uploaders (player_account_id, first_bundle_at_ms)
           VALUES (?1, ?2) ON CONFLICT(player_account_id) DO NOTHING`,
        )
        .bind(descriptor.uploaderAccountId, times.availableAtMs),
    },
  ];
  if (statements.at(-1)?.name !== "insert_uploader") {
    throw new Error("Bundle uploader must be the final commit statement");
  }
  return statements;
}

function readProjection(
  statements: readonly NamedStatement[],
  results: readonly D1Result<unknown>[],
): { eligible: number; inserted: number } {
  let eligible = 0;
  let inserted = 0;
  for (const [index, definition] of statements.entries()) {
    const result = results[index];
    if (definition.read === "eligible") {
      eligible = Number((result?.results?.[0] as { eligible?: number } | undefined)?.eligible ?? 0);
    } else if (definition.read === "inserted") {
      // Transitional compatibility triggers also write rows; count only our inserts.
      inserted = result?.results.length ?? 0;
    }
  }
  return { eligible, inserted };
}

export async function commitBundle(
  db: D1Database,
  descriptor: ValidatedBundleDescriptor,
  digest: string,
  times: CommitTimes,
  observer: CommitObserver = DEFAULT_OBSERVER,
): Promise<CommitOutcome> {
  const statements = buildStatements(db, descriptor, digest, times);
  let results: D1Result<unknown>[];
  try {
    results = await db.batch(statements.map(({ statement }) => statement));
  } catch {
    try {
      const existing = await inspectExistingBundle(db, descriptor, digest);
      if (existing !== null) return existing;
    } catch {
      // The stable commit failure below owns read-after-failure errors too.
    }
    throw new HttpError(503, "storage_unavailable", "Bundle index commit failed", true);
  }

  const projection = readProjection(statements, results);
  if (projection.inserted < projection.eligible) {
    observer.projectionDuplicate({
      bundle_id: descriptor.bundleId,
      dropped: projection.eligible - projection.inserted,
    });
  }
  return { kind: "committed", projection };
}
