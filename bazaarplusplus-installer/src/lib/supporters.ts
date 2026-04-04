import { loadSupporters as loadSupportersFromTauri } from './installer/api.ts';
import { hasTauriRuntime as detectTauriRuntime } from './installer/runtime.ts';
import type {
  SupporterEntry,
  SupporterTierId,
  SupportersResponse
} from './types.ts';

const SUPPORTER_TIERS: readonly SupporterTierId[] = [1, 2, 3, 4];
const BUNDLED_SUPPORTERS_PATH = '/support/supporter-list.json';
let cachedSupportersSnapshot: {
  key: string;
  response: SupportersResponse;
} | null = null;

type LoadSupportersDataOptions = {
  hasTauriRuntime?: boolean;
  fetchImpl?: typeof fetch;
  loadFromTauri?: () => Promise<SupportersResponse>;
  randomFn?: () => number;
};

export function normalizeSupporterPayload(payload: unknown): SupporterEntry[] {
  const entries = Array.isArray(payload) ? payload : [];

  return sortSupporters(
    entries
      .map((entry) => normalizeSupporterEntry(entry))
      .filter((entry): entry is SupporterEntry => entry !== null)
  );
}

export function sortSupporters(entries: SupporterEntry[]): SupporterEntry[] {
  return entries
    .slice()
    .sort(
      (left, right) =>
        right.tier - left.tier || left.name.localeCompare(right.name)
    );
}

export function shuffleSupportersWithinTier(
  entries: SupporterEntry[],
  randomFn: () => number = Math.random
): SupporterEntry[] {
  const tierGroups = new Map<SupporterTierId, SupporterEntry[]>(
    SUPPORTER_TIERS.map((tier) => [tier, [] as SupporterEntry[]])
  );

  for (const entry of sortSupporters(entries)) {
    tierGroups.get(entry.tier)?.push(entry);
  }

  for (const tier of SUPPORTER_TIERS) {
    shuffleInPlace(tierGroups.get(tier) ?? [], randomFn);
  }

  return SUPPORTER_TIERS.slice()
    .reverse()
    .flatMap((tier) => tierGroups.get(tier) ?? []);
}

export function resetSupportersDataCache(): void {
  cachedSupportersSnapshot = null;
}

export async function loadSupportersData(
  options: LoadSupportersDataOptions = {}
): Promise<SupportersResponse> {
  const hasTauriRuntime = options.hasTauriRuntime ?? detectTauriRuntime();
  const randomFn = options.randomFn ?? Math.random;

  if (hasTauriRuntime) {
    try {
      const payload = await (
        options.loadFromTauri ?? loadSupportersFromTauri
      )();
      return finalizeSupportersResponse(payload, randomFn);
    } catch {
      return loadBundledSupporters(options.fetchImpl, randomFn);
    }
  }

  return loadBundledSupporters(options.fetchImpl, randomFn);
}

function normalizeSupporterTier(value: unknown): SupporterTierId | null {
  if (typeof value !== 'number' || !Number.isInteger(value)) return null;

  const matchedTier = SUPPORTER_TIERS.find((tierId) => tierId === value);

  return matchedTier ?? null;
}

function normalizeSupporterEntry(value: unknown): SupporterEntry | null {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const { name, tier } = value as { name?: unknown; tier?: unknown };
  if (typeof name !== 'string' || !name.trim()) {
    return null;
  }

  const normalizedTier = normalizeSupporterTier(tier);
  if (normalizedTier == null) {
    return null;
  }

  return {
    name: name.trim(),
    tier: normalizedTier
  };
}

async function loadBundledSupporters(
  fetchImpl: typeof fetch = fetch,
  randomFn: () => number = Math.random
): Promise<SupportersResponse> {
  const response = await fetchImpl(BUNDLED_SUPPORTERS_PATH);
  if (!response.ok) {
    throw new Error(`Failed to load supporter list: ${response.status}`);
  }

  const payload = await response.json();
  const bundledResponse = {
    entries: normalizeSupporterPayload(payload),
    source: 'bundled',
    fetchedAt: null,
    stale: false
  } satisfies SupportersResponse;

  return finalizeSupportersResponse(bundledResponse, randomFn);
}

function cloneSupportersResponse(
  payload: SupportersResponse
): SupportersResponse {
  return {
    ...payload,
    entries: payload.entries.slice()
  };
}

function finalizeSupportersResponse(
  payload: SupportersResponse,
  randomFn: () => number = Math.random
): SupportersResponse {
  const snapshotKey = createSupportersSnapshotKey(payload);

  if (cachedSupportersSnapshot?.key === snapshotKey) {
    return cloneSupportersResponse(cachedSupportersSnapshot.response);
  }

  const shuffledResponse = {
    ...payload,
    entries: shuffleSupportersWithinTier(payload.entries, randomFn)
  };

  cachedSupportersSnapshot = {
    key: snapshotKey,
    response: shuffledResponse
  };

  return cloneSupportersResponse(shuffledResponse);
}

function createSupportersSnapshotKey(payload: SupportersResponse): string {
  return JSON.stringify({
    fetchedAt: payload.fetchedAt,
    entries: sortSupporters(payload.entries).map((entry) => [
      entry.name,
      entry.tier
    ])
  });
}

function shuffleInPlace(
  entries: SupporterEntry[],
  randomFn: () => number
): void {
  for (let index = entries.length - 1; index > 0; index -= 1) {
    const swapIndex = Math.floor(randomFn() * (index + 1));
    [entries[index], entries[swapIndex]] = [entries[swapIndex], entries[index]];
  }
}
