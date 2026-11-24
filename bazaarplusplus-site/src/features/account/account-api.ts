/** The Website account API: `handleWebApiRequest` in bazaarplusplus-server owns it. */

export type RunSummary = {
  run_id: string;
  hero: string | null;
  victories: number | null;
  losses: number | null;
  final_player_rank: string | null;
  final_player_rating: number | null;
  final_player_rating_delta: number | null;
  game_mode: string | null;
  ended_at_ms: number | null;
  has_detail: boolean;
};

export type HeroStats = {
  hero: string;
  runs: number;
  avg_victories: number;
  best_victories: number;
};

export type RatingPoint = {
  ended_at_ms: number;
  rating: number;
  rank: string | null;
};

export type Me = {
  email: string;
  game: { user_name: string; linked_at_ms: number } | null;
  stats: {
    runs: number;
    victories: number;
    ten_win_runs: number;
    heroes: HeroStats[];
    rating: RatingPoint[];
  };
};

export type RunsPage = { runs: RunSummary[]; next_cursor: string | null };

export type BindCode = { code: string; expires_at_ms: number };

export class AccountHttpError extends Error {
  readonly status: number;
  /** `error.code` of the server's error envelope, when it sent one. */
  readonly code: string | null;

  constructor(status: number, code: string | null) {
    super(code ?? String(status));
    this.status = status;
    this.code = code;
  }
}

/** The session is gone: the page hands over to sign-in. */
export function isSignedOut(error: unknown): boolean {
  return (
    error instanceof AccountHttpError &&
    error.status === 401 &&
    (error.code === null || error.code === 'unauthorized')
  );
}

export function errorCode(error: unknown): string | null {
  return error instanceof AccountHttpError ? error.code : null;
}

async function request(path: string, init: RequestInit): Promise<Response> {
  const response = await fetch('/api/' + path, {
    credentials: 'same-origin',
    ...init
  });
  if (!response.ok) {
    let code: string | null = null;
    try {
      const body = (await response.json()) as { error?: { code?: unknown } };
      code = typeof body.error?.code === 'string' ? body.error.code : null;
    } catch {
      code = null;
    }
    throw new AccountHttpError(response.status, code);
  }
  return response;
}

async function getJson<T>(path: string, signal: AbortSignal): Promise<T> {
  const response = await request(path, { signal, cache: 'no-store' });
  return response.json() as Promise<T>;
}

export function loadMe(signal: AbortSignal): Promise<Me> {
  return getJson('me', signal);
}

export function loadRuns(
  cursor: string | null,
  signal: AbortSignal
): Promise<RunsPage> {
  const query = cursor ? '?' + new URLSearchParams({ cursor }).toString() : '';
  return getJson('runs' + query, signal);
}

export async function loadRun(
  runId: string,
  signal: AbortSignal
): Promise<RunSummary> {
  const body = await getJson<{ run: RunSummary }>(
    `runs/${encodeURIComponent(runId)}`,
    signal
  );
  return body.run;
}

/** The stored Run segment bytes (`application/x-bpp-run-v6`). */
export async function loadRunPayload(
  runId: string,
  signal: AbortSignal
): Promise<Uint8Array> {
  const response = await request(`runs/${encodeURIComponent(runId)}/payload`, {
    signal
  });
  return new Uint8Array(await response.arrayBuffer());
}

type PostPath =
  | 'register/start'
  | 'register/confirm'
  | 'login'
  | 'password/reset/start'
  | 'password/reset/confirm'
  | 'logout'
  | 'bind-code'
  | 'unlink'
  | 'account/delete';

export async function postAccount<T = unknown>(
  path: PostPath,
  data: Record<string, string> = {}
): Promise<T> {
  const response = await request(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data)
  });
  const text = await response.text();
  return (text ? JSON.parse(text) : null) as T;
}
