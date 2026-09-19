export type Supporter = {
  name: string;
  tier: number;
};

export const SUPPORTER_LIST_URL = 'https://bpp-static.bazaarplusplus.com/supporter-list.json';

function isSupporter(value: unknown): value is Supporter {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const candidate = value as Record<string, unknown>;
  return typeof candidate.name === 'string' && typeof candidate.tier === 'number';
}

export async function loadSupporters(signal?: AbortSignal): Promise<Supporter[]> {
  const response = await fetch(SUPPORTER_LIST_URL, {
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(`supporter-list.json responded with ${response.status}`);
  }

  const payload: unknown = await response.json();

  if (!Array.isArray(payload)) {
    throw new Error('supporter-list.json is not an array');
  }

  return payload.filter(isSupporter);
}

const defaultTierDisplayOrder = [4, 3, 2, 1];

function fisherYatesShuffle<T>(items: T[], random: () => number): T[] {
  const shuffled = [...items];

  for (let index = shuffled.length - 1; index > 0; index -= 1) {
    const swapIndex = Math.floor(random() * (index + 1));
    const current = shuffled[index];
    shuffled[index] = shuffled[swapIndex];
    shuffled[swapIndex] = current;
  }

  return shuffled;
}

export function orderSupportersForDisplay(
  supportersInput: Supporter[],
  random: () => number = Math.random
): Supporter[] {
  const supportersByTier = new Map<number, Supporter[]>();

  for (const supporter of supportersInput) {
    const list = supportersByTier.get(supporter.tier);

    if (list) {
      list.push(supporter);
      continue;
    }

    supportersByTier.set(supporter.tier, [supporter]);
  }

  const extraTierOrder = [...supportersByTier.keys()]
    .filter((tier) => !defaultTierDisplayOrder.includes(tier))
    .sort((left, right) => right - left);
  const tierDisplayOrder = [...defaultTierDisplayOrder, ...extraTierOrder];

  return tierDisplayOrder.flatMap((tier) => {
    const list = supportersByTier.get(tier);
    return list ? fisherYatesShuffle(list, random) : [];
  });
}
