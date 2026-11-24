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
};

type RecordsPage = {
  user_name: string;
  email_masked: string;
  runs: RunSummary[];
  next_cursor: string | null;
};

export class AccountHttpError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(String(status));
    this.status = status;
  }
}

async function request(path: string, init: RequestInit) {
  const response = await fetch('/api/' + path, {
    credentials: 'same-origin',
    ...init
  });
  if (!response.ok) throw new AccountHttpError(response.status);
  return response;
}

export async function loadRecords(
  cursor: string | null,
  signal: AbortSignal
): Promise<RecordsPage> {
  const query = cursor ? '?' + new URLSearchParams({ cursor }).toString() : '';
  const response = await request('me' + query, { signal, cache: 'no-store' });
  return response.json() as Promise<RecordsPage>;
}

export async function postAccount(
  path: 'login/start' | 'login/confirm' | 'logout' | 'unbind',
  data = {}
): Promise<void> {
  await request(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data)
  });
}
