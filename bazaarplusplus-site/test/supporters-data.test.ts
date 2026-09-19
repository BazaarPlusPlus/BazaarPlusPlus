import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';

import {
  loadSupporters,
  orderSupportersForDisplay,
  SUPPORTER_LIST_URL,
  type Supporter,
} from '../src/features/support/supporters-data';

function fixedRandom(values: number[]): () => number {
  let index = 0;
  return () => {
    const next = values[index] ?? 0;
    index += 1;
    return next;
  };
}

describe('orderSupportersForDisplay', () => {
  test('groups by tier with the canonical 4→1 order', () => {
    const supporters: Supporter[] = [
      { name: 'A', tier: 1 },
      { name: 'B', tier: 4 },
      { name: 'C', tier: 3 },
      { name: 'D', tier: 2 },
    ];

    const tiers = orderSupportersForDisplay(supporters, fixedRandom([0, 0, 0, 0])).map(
      (s) => s.tier
    );

    expect(tiers).toEqual([4, 3, 2, 1]);
  });

  test('appends unknown tiers after the canonical order, descending', () => {
    const supporters: Supporter[] = [
      { name: 'A', tier: 4 },
      { name: 'B', tier: 5 },
      { name: 'C', tier: 7 },
    ];

    const tiers = orderSupportersForDisplay(supporters, fixedRandom([0, 0, 0])).map((s) => s.tier);

    expect(tiers).toEqual([4, 7, 5]);
  });

  test('shuffles within a tier using the supplied random source', () => {
    const supporters: Supporter[] = [
      { name: 'A', tier: 4 },
      { name: 'B', tier: 4 },
      { name: 'C', tier: 4 },
    ];

    const ordered = orderSupportersForDisplay(supporters, fixedRandom([0, 0])).map((s) => s.name);

    expect(ordered.sort()).toEqual(['A', 'B', 'C']);
  });

  test('returns empty array for empty input', () => {
    expect(orderSupportersForDisplay([])).toEqual([]);
  });
});

describe('loadSupporters', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  test('parses the remote supporter list into typed entries', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify([
            { name: 'A', tier: 4 },
            { name: 'B', tier: 3 },
          ]),
          { status: 200, headers: { 'content-type': 'application/json' } }
        )
      )
    );

    const supporters = await loadSupporters();

    expect(supporters).toEqual([
      { name: 'A', tier: 4 },
      { name: 'B', tier: 3 },
    ]);
    expect(fetch).toHaveBeenCalledWith(
      SUPPORTER_LIST_URL,
      expect.objectContaining({ headers: { Accept: 'application/json' } })
    );
  });

  test('drops malformed entries instead of throwing', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          new Response(
            JSON.stringify([
              { name: 'good', tier: 4 },
              { name: 'no-tier' },
              null,
              { tier: 3 },
              'string-entry',
            ]),
            { status: 200, headers: { 'content-type': 'application/json' } }
          )
        )
    );

    expect(await loadSupporters()).toEqual([{ name: 'good', tier: 4 }]);
  });

  test('throws on non-2xx response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('boom', { status: 503 })));

    await expect(loadSupporters()).rejects.toThrow(/503/);
  });

  test('throws when the payload is not an array', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ supporters: [] }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        })
      )
    );

    await expect(loadSupporters()).rejects.toThrow(/not an array/);
  });
});
