import { validIdentifier } from '../bundle/manifest';
import { apiError } from '../errors';

// Hardcoded upstream origin and read-only path; never accept a client-supplied URL.
export const OFFICIAL_ACCOUNT_SUMMARY_URL =
  'https://enter.playthebazaar.com/api/Accounts/me/summary';

const OFFICIAL_API_TIMEOUT_MS = 10_000;

export interface OfficialGameAccount {
  readonly id: string;
  readonly userName: string;
}

export interface VerifyGameAccountInput {
  readonly gameToken: string;
  readonly platform: string;
  readonly flavor: string;
}

export async function verifyOfficialGameAccount(
  input: VerifyGameAccountInput
): Promise<OfficialGameAccount> {
  let response: Response;
  try {
    response = await fetch(OFFICIAL_ACCOUNT_SUMMARY_URL, {
      method: 'GET',
      headers: {
        Authorization: `Bearer ${input.gameToken}`,
        'X-Platform': input.platform,
        'X-ClientFlavor': input.flavor,
        Accept: 'application/json'
      },
      signal: AbortSignal.timeout(OFFICIAL_API_TIMEOUT_MS)
    });
  } catch {
    throw apiError(
      'game_api_unavailable',
      'Official game API is unavailable or timed out'
    );
  }

  if (response.status === 401 || response.status === 403) {
    throw apiError(
      'game_token_rejected',
      'Official game API rejected the game access token'
    );
  }
  if (response.status !== 200) {
    throw apiError(
      'game_api_unavailable',
      'Official game API returned an unexpected status'
    );
  }

  let body: unknown;
  try {
    body = await response.json();
  } catch {
    throw apiError(
      'game_api_unavailable',
      'Official game API returned an invalid response'
    );
  }

  if (typeof body !== 'object' || body === null || Array.isArray(body)) {
    throw apiError(
      'game_api_unavailable',
      'Official game API returned an unexpected payload'
    );
  }

  const record = body as Record<string, unknown>;
  const id = typeof record.id === 'string' ? record.id.trim() : '';
  const rawUserName =
    typeof record.userName === 'string' ? record.userName.trim() : '';
  if (!validIdentifier(id) || rawUserName.length === 0) {
    throw apiError(
      'game_api_unavailable',
      'Official game API response lacked account identity'
    );
  }

  // Keep only id and userName; discard email and all other fields immediately.
  return {
    id,
    userName: rawUserName.slice(0, 128)
  };
}
