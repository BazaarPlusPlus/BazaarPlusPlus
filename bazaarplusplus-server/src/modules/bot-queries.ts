import { validBundleId, validIdentifier } from '../bundle/manifest';
import type { Env } from '../env';
import { apiError } from '../errors';
import { oneQueryValue } from '../http/request';
import type { BotBundlesResponse, BotPlayersResponse } from '../http/schemas';
import { createBundleDownloadSigner, signDownloadPage } from '../presigner';

const DAY = 86_400_000;
const PAGE_SIZE = 100;
const recentStart = (now: number) =>
  Math.floor((now + DAY / 3) / DAY) * DAY - DAY / 3 - 2 * DAY;
function query(request: Request, allowed: string[]): URLSearchParams {
  const values = new URL(request.url).searchParams;
  for (const key of values.keys()) {
    if (!allowed.includes(key))
      throw apiError('invalid_query', `Unknown query parameter: ${key}`);
    oneQueryValue(values, key, false);
  }
  return values;
}
function integer(value: string | null, fallback: number): number {
  if (value === null) return fallback;
  if (
    !/^(0|[1-9][0-9]*)$/.test(value) ||
    !Number.isSafeInteger(Number(value))
  ) {
    throw apiError('invalid_query', 'Expected a non-negative safe integer');
  }
  return Number(value);
}
export async function findBotPlayers(
  request: Request,
  env: Env
): Promise<BotPlayersResponse> {
  const values = query(request, ['name']);
  const name = oneQueryValue(values, 'name', true)?.trim();
  if (!name || name.length > 128)
    throw apiError('invalid_query', 'name must contain 1–128 characters');
  const now = Date.now();
  try {
    const rows = await env.DB.prepare(`
      SELECT g.uploader_account_id AS account_id, g.player_display_name AS display_name
      FROM ghost_battle_summaries g INDEXED BY idx_ghost_player_name
      JOIN bundles b ON b.bundle_id = g.bundle_id
      WHERE g.player_display_name = ?1 AND b.stored_at_ms >= ?2 AND b.stored_at_ms < ?3
      GROUP BY g.uploader_account_id ORDER BY g.uploader_account_id LIMIT 21
    `)
      .bind(name, recentStart(now), now)
      .all<{ account_id: string; display_name: string }>();
    return {
      players: rows.results.slice(0, 20),
      has_more: rows.results.length > 20
    };
  } catch {
    throw apiError('storage_unavailable', 'Player name index query failed');
  }
}
export async function collectBotBundles(
  request: Request,
  env: Env
): Promise<BotBundlesResponse> {
  const values = query(request, [
    'account_id',
    'stored_from_ms',
    'stored_before_ms',
    'after_stored_at_ms',
    'after_bundle_id'
  ]);
  const account = oneQueryValue(values, 'account_id', true);
  if (!account || !validIdentifier(account))
    throw apiError('invalid_query', 'Invalid account_id');
  const now = Date.now();
  const from = integer(values.get('stored_from_ms'), recentStart(now));
  const before = integer(values.get('stored_before_ms'), now);
  if (from < now - 3 * DAY - 5 * 60_000 || from >= before || before > now)
    throw apiError(
      'invalid_query',
      'Candidate window exceeds three days plus five minutes of queue time'
    );
  const cursorTime = values.get('after_stored_at_ms');
  const cursorId = values.get('after_bundle_id');
  if ((cursorTime === null) !== (cursorId === null))
    throw apiError('invalid_query', 'Both keyset fields are required');
  const after = integer(cursorTime, from);
  if (
    after < from ||
    after >= before ||
    (cursorId !== null && !validBundleId(cursorId))
  )
    throw apiError('invalid_query', 'Invalid keyset position');
  let rows: {
    bundle_id: string;
    run_id: string;
    stored_at_ms: number;
    object_key: string;
    sha256: string;
  }[];
  try {
    // Stored time over-selects candidates, including late uploads. Only the
    // consumer's decoded Run.EndedAtMs determines membership in the report.
    const result = await env.DB.prepare(`
      SELECT bundle_id, run_id, stored_at_ms, object_key, bundle_sha256 AS sha256
      FROM bundles INDEXED BY idx_bundles_uploader_stored
      WHERE uploader_account_id = ?1 AND stored_at_ms >= ?2 AND stored_at_ms < ?3
        AND (stored_at_ms, bundle_id) > (?4, ?5)
      ORDER BY stored_at_ms, bundle_id LIMIT ?6
    `)
      .bind(account, from, before, after, cursorId ?? '', PAGE_SIZE + 1)
      .all<(typeof rows)[number]>();
    rows = result.results;
  } catch {
    throw apiError('storage_unavailable', 'Player Bundle query failed');
  }
  const page = rows.slice(0, PAGE_SIZE);
  const downloads = await signDownloadPage(
    createBundleDownloadSigner(env),
    page.map((row) => row.object_key),
    now,
    'Bundle URL signing failed'
  );
  const last = page.at(-1);
  return {
    window: { stored_from_ms: from, stored_before_ms: before },
    items: page.map((row, i) => ({
      bundle_id: row.bundle_id,
      run_id: row.run_id,
      stored_at_ms: row.stored_at_ms,
      sha256: row.sha256,
      download_url: downloads[i].url,
      download_expires_at_ms: downloads[i].expiresAtMs
    })),
    next_after:
      rows.length > PAGE_SIZE && last
        ? { stored_at_ms: last.stored_at_ms, bundle_id: last.bundle_id }
        : null
  };
}
