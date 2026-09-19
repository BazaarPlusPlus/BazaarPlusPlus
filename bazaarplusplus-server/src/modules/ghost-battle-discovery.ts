import { toHex } from "../bundle/hex";
import { validAccountId } from "../bundle/manifest";
import type { Env } from "../env";
import { HttpError } from "../errors";
import type { HandlerDeps } from "../http/deps";
import { oneQueryValue } from "../http/request";
import { GHOST_DEFAULT_LIMIT, GHOST_LOOKBACK_MS, GHOST_MAX_LIMIT } from "../limits";
import { logEvent } from "../observability";
import { signDownloadPage } from "../presigner";

interface GhostBattleSummary {
  battle_id: string;
  bundle_id: string;
  recorded_at_ms: number;
  is_final_battle: boolean;
  day: number;
  hour: number;
  result: string;
  winner_combatant_id: string | null;
  player: {
    account_id: string;
    display_name: string;
    hero_name: string | null;
    rank: string | null;
    rating: number | null;
  };
  opponent: {
    account_id: string;
    hero_name: string | null;
  };
  download_url: string;
  download_expires_at_ms: number;
}

export interface GhostDiscoveryResponse {
  battles: GhostBattleSummary[];
}

interface GhostRow {
  uploader_account_id: string;
  battle_id: string;
  bundle_id: string;
  opponent_account_id: string;
  recorded_at_ms: number;
  is_final_battle: number;
  day: number;
  hour: number;
  result: string;
  winner_combatant_id: string | null;
  player_display_name: string;
  player_hero_name: string | null;
  opponent_hero_name: string | null;
  player_rank: string | null;
  player_rating: number | null;
  object_key: string;
}

async function accountHash(accountId: string): Promise<string> {
  const bytes = new Uint8Array(
    await crypto.subtle.digest("SHA-256", new TextEncoder().encode(accountId)),
  );
  return toHex(bytes.subarray(0, 8));
}

export async function discoverGhostBattles(
  request: Request,
  env: Env,
  requestId: string,
  deps: HandlerDeps,
): Promise<GhostDiscoveryResponse> {
  const rateLimitKey = request.headers.get("CF-Connecting-IP") ?? "unknown";
  let rateLimit: RateLimitOutcome;
  try {
    rateLimit = await env.GHOST_BATTLE_RATE_LIMITER.limit({ key: rateLimitKey });
  } catch {
    throw new HttpError(503, "storage_unavailable", "Ghost rate limiter is unavailable", true);
  }
  if (!rateLimit.success) {
    logEvent("ghost.discovery", { request_id: requestId, limited: true });
    throw new HttpError(
      429,
      "rate_limited",
      "Ghost Battle request rate exceeded",
      true,
      undefined,
      { "Retry-After": "60" },
    );
  }

  const url = new URL(request.url);
  const allowed = new Set(["player_account_id", "limit"]);
  for (const key of url.searchParams.keys()) {
    if (!allowed.has(key)) {
      throw new HttpError(400, "invalid_query", `Unknown query parameter: ${key}`, false, {
        field: key,
      });
    }
  }
  const accountId = oneQueryValue(url.searchParams, "player_account_id", true);
  if (accountId === null || !validAccountId(accountId)) {
    throw new HttpError(400, "invalid_query", "player_account_id is invalid", false, {
      field: "player_account_id",
    });
  }
  const limitValue = oneQueryValue(url.searchParams, "limit", false);
  let limit = GHOST_DEFAULT_LIMIT;
  if (limitValue !== null) {
    if (!/^[1-9][0-9]*$/.test(limitValue)) {
      throw new HttpError(400, "invalid_query", "limit must be an integer", false, {
        field: "limit",
      });
    }
    limit = Number(limitValue);
  }
  if (!Number.isSafeInteger(limit) || limit < 1 || limit > GHOST_MAX_LIMIT) {
    throw new HttpError(400, "invalid_query", "limit must be between 1 and 200", false, {
      field: "limit",
    });
  }

  const issuedAt = deps.now();
  let rows: GhostRow[];
  try {
    const result = await env.DB.prepare(
      `SELECT
         g.battle_id,
         g.bundle_id,
         g.recorded_at_ms,
         g.is_final_battle,
         g.uploader_account_id, g.opponent_account_id,
         g.day, g.hour, g.result, g.winner_combatant_id,
         g.player_display_name, g.player_hero_name, g.opponent_hero_name,
         g.player_rank, g.player_rating,
         x.object_key
       FROM ghost_battle_summaries AS g INDEXED BY idx_ghost_summaries_query
       JOIN bundles AS x ON x.bundle_id = g.bundle_id
       WHERE g.opponent_account_id = ?1
         AND g.recorded_at_ms >= ?2
       ORDER BY g.recorded_at_ms DESC, g.battle_id DESC
       LIMIT ?3`,
    )
      .bind(accountId, issuedAt - GHOST_LOOKBACK_MS, limit)
      .all<GhostRow>();
    rows = result.results;
  } catch {
    throw new HttpError(503, "storage_unavailable", "Ghost Battle query failed", true);
  }

  const downloads = await signDownloadPage(
    deps.signer,
    rows.map((row) => row.object_key),
    issuedAt,
    "Ghost Battle URL signing failed",
  );
  const battles = rows.map((row, index): GhostBattleSummary => {
    return {
      battle_id: row.battle_id,
      bundle_id: row.bundle_id,
      recorded_at_ms: row.recorded_at_ms,
      is_final_battle: row.is_final_battle === 1,
      day: row.day,
      hour: row.hour,
      result: row.result,
      winner_combatant_id: row.winner_combatant_id,
      player: {
        account_id: row.uploader_account_id,
        display_name: row.player_display_name,
        hero_name: row.player_hero_name,
        rank: row.player_rank,
        rating: row.player_rating,
      },
      opponent: {
        account_id: row.opponent_account_id,
        hero_name: row.opponent_hero_name,
      },
      download_url: downloads[index].url,
      download_expires_at_ms: downloads[index].expiresAtMs,
    };
  });
  logEvent("ghost.discovery", {
    request_id: requestId,
    account_hash: await accountHash(accountId),
    row_count: rows.length,
    limited: false,
  });
  return { battles };
}
