import type { ValidatedBundleDescriptor } from '../bundle/manifest';
import { apiError, type HttpError } from '../errors';
import type { BundleReceipt } from '../http/schemas';
import { logEvent } from '../observability';

export type CommitOutcome =
  | { kind: 'stored' | 'duplicate'; receipt: BundleReceipt }
  | { kind: 'conflict'; reason: ConflictReason };

type ConflictReason = 'bundle_id_conflict' | 'run_already_bundled';

export interface CommitTimes {
  availableAtMs: number;
  storedAtMs: number;
  verifiedAccountId?: string | null;
  // The BPP Account the Device Token belongs to; the Run Summary is written only
  // while that account is still the one linked to the game account.
  verifiedBppAccountId?: string | null;
  // True when the Run segment was copied to the linked account's run detail.
  detailStored?: boolean;
}

interface ExistingBundleRow {
  bundle_id: string;
  run_id: string;
  bundle_sha256: string;
}

type StatementName =
  | 'insert_bundle'
  | 'insert_ghost'
  | 'count_eligible'
  | 'insert_run_summary'
  | 'insert_uploader';

interface NamedStatement {
  name: StatementName;
  statement: D1PreparedStatement;
  read?: 'inserted' | 'eligible';
}

const ELIGIBILITY_PREDICATE = `
  json_extract(value, '$.opponent.account_id') = ?2
  OR EXISTS (
    SELECT 1 FROM bundle_uploaders
    WHERE player_account_id = json_extract(value, '$.opponent.account_id')
  )`;

function receipt(
  outcome: BundleReceipt['outcome'],
  bundleId: string,
  runId: string
): BundleReceipt {
  return { bundle_id: bundleId, run_id: runId, outcome };
}

export function conflictError(reason: ConflictReason): HttpError {
  return reason === 'bundle_id_conflict'
    ? apiError(reason, 'Bundle ID already has different bytes')
    : apiError(reason, 'Run already belongs to another Bundle');
}

function decideFromExisting(
  descriptor: ValidatedBundleDescriptor,
  digest: string,
  bundle: ExistingBundleRow | null,
  runBundleId: string | null
): CommitOutcome | null {
  if (bundle !== null) {
    return bundle.bundle_sha256 === digest
      ? {
          kind: 'duplicate',
          receipt: receipt('duplicate', bundle.bundle_id, bundle.run_id)
        }
      : { kind: 'conflict', reason: 'bundle_id_conflict' };
  }
  if (runBundleId !== null && runBundleId !== descriptor.bundleId) {
    return { kind: 'conflict', reason: 'run_already_bundled' };
  }
  return null;
}

export async function inspectExistingBundle(
  db: D1Database,
  descriptor: ValidatedBundleDescriptor,
  digest: string
): Promise<CommitOutcome | null> {
  const [bundle, run] = await Promise.all([
    db
      .prepare(
        `SELECT bundle_id, run_id, bundle_sha256 FROM bundles WHERE bundle_id = ?1`
      )
      .bind(descriptor.bundleId)
      .first<ExistingBundleRow>(),
    db
      .prepare(`SELECT bundle_id FROM bundles WHERE run_id = ?1`)
      .bind(descriptor.runId)
      .first<{ bundle_id: string }>()
  ]);
  return decideFromExisting(
    descriptor,
    digest,
    bundle ?? null,
    run?.bundle_id ?? null
  );
}

function buildStatements(
  db: D1Database,
  descriptor: ValidatedBundleDescriptor,
  digest: string,
  times: CommitTimes
): NamedStatement[] {
  const battles = JSON.stringify(descriptor.battles);
  return [
    {
      name: 'insert_bundle',
      statement: db
        .prepare(
          `INSERT INTO bundles (
            bundle_id, run_id, uploader_account_id, object_key, bundle_sha256,
            object_bytes, client_created_at_ms, stored_at_ms, available_at_ms, has_screenshot
          ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)`
        )
        .bind(
          descriptor.bundleId,
          descriptor.runId,
          descriptor.uploaderAccountId,
          descriptor.objectKey,
          digest,
          descriptor.objectBytes,
          descriptor.createdAtMs,
          times.storedAtMs,
          times.availableAtMs,
          descriptor.screenshot === null ? 0 : 1
        )
    },
    {
      name: 'insert_ghost',
      read: 'inserted',
      statement: db
        .prepare(
          `INSERT INTO ghost_battle_summaries (
            uploader_account_id, battle_id, bundle_id, opponent_account_id,
            recorded_at_ms, is_final_battle, day, hour, result, winner_combatant_id,
            player_display_name, player_hero_name, opponent_hero_name, player_rank, player_rating,
            opponent_level, opponent_prestige, opponent_victories, object_key
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
                 json_extract(value, '$.player.rating'),
                 json_extract(value, '$.opponent.level'),
                 json_extract(value, '$.opponent.prestige'),
                 json_extract(value, '$.opponent.victories'),
                 ?4
          FROM json_each(?1)
          WHERE ${ELIGIBILITY_PREDICATE}
          ON CONFLICT(uploader_account_id, battle_id) DO NOTHING
          RETURNING battle_id`
        )
        .bind(
          battles,
          descriptor.uploaderAccountId,
          descriptor.bundleId,
          descriptor.objectKey
        )
    },
    {
      name: 'count_eligible',
      read: 'eligible',
      statement: db
        .prepare(
          `SELECT COUNT(*) AS eligible
           FROM json_each(?1)
           WHERE ${ELIGIBILITY_PREDICATE}`
        )
        .bind(battles, descriptor.uploaderAccountId)
    },
    ...(times.verifiedAccountId === descriptor.uploaderAccountId &&
    descriptor.runProjection !== null
      ? [
          {
            name: 'insert_run_summary' as const,
            statement: db
              .prepare(
                `INSERT INTO run_summaries (
                  run_id, game_account_id, status, game_mode, hero,
                  started_at_ms, ended_at_ms, day, victories, losses,
                  final_player_rank, final_player_rating, final_player_rating_delta, stored_at_ms,
                  detail_stored
                )
                SELECT ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15
                WHERE EXISTS (
                  SELECT 1 FROM bpp_accounts
                  WHERE game_account_id = ?2 AND account_id = ?16
                )
                ON CONFLICT(run_id) DO NOTHING`
              )
              .bind(
                descriptor.runId,
                descriptor.uploaderAccountId,
                descriptor.runProjection.status,
                descriptor.runProjection.game_mode,
                descriptor.runProjection.hero,
                descriptor.runProjection.started_at_ms,
                descriptor.runProjection.ended_at_ms,
                descriptor.runProjection.day,
                descriptor.runProjection.victories,
                descriptor.runProjection.losses,
                descriptor.runProjection.final_player_rank,
                descriptor.runProjection.final_player_rating,
                descriptor.runProjection.final_player_rating_delta,
                times.storedAtMs,
                times.detailStored === true ? 1 : 0,
                times.verifiedBppAccountId ?? null
              )
          }
        ]
      : []),
    {
      name: 'insert_uploader',
      statement: db
        .prepare(
          `INSERT INTO bundle_uploaders (player_account_id)
           VALUES (?1) ON CONFLICT(player_account_id) DO NOTHING`
        )
        .bind(descriptor.uploaderAccountId)
    }
  ];
}

function readProjection(
  statements: readonly NamedStatement[],
  results: readonly D1Result[]
): { eligible: number; inserted: number } {
  let eligible = 0;
  let inserted = 0;
  for (const [index, definition] of statements.entries()) {
    const result = results[index];
    if (definition.read === 'eligible') {
      eligible = Number(
        (result?.results?.[0] as { eligible?: unknown } | undefined)
          ?.eligible ?? 0
      );
    } else if (definition.read === 'inserted') {
      inserted = result?.results.length ?? 0;
    }
  }
  return { eligible, inserted };
}

export async function commitBundle(
  db: D1Database,
  descriptor: ValidatedBundleDescriptor,
  digest: string,
  times: CommitTimes
): Promise<CommitOutcome> {
  const statements = buildStatements(db, descriptor, digest, times);
  let results: D1Result[];
  try {
    results = await db.batch(statements.map(({ statement }) => statement));
  } catch {
    try {
      const existing = await inspectExistingBundle(db, descriptor, digest);
      if (existing !== null) return existing;
    } catch {
      // The stable commit failure below owns read-after-failure errors too.
    }
    throw apiError('storage_unavailable', 'Bundle index commit failed');
  }

  const projection = readProjection(statements, results);
  if (projection.inserted < projection.eligible) {
    logEvent('bundle.projection.duplicate', {
      bundle_id: descriptor.bundleId,
      dropped: projection.eligible - projection.inserted
    });
  }
  return {
    kind: 'stored',
    receipt: receipt('stored', descriptor.bundleId, descriptor.runId)
  };
}
