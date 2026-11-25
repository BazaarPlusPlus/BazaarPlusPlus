import { expect, it, vi, afterEach } from 'vite-plus/test';
import { cardTextFile, loadCardText } from '../src/features/runs/card-text';
const en = 'text-en-00000000000000000000.json';
const zh = 'text-zh-11111111111111111111.json';
afterEach(() => vi.unstubAllGlobals());
it('chooses the locale file and falls back only for an absent Chinese file', () => {
  expect(cardTextFile({ en, zh }, 'zh')).toBe(zh);
  expect(cardTextFile({ en, zh }, 'en')).toBe(en);
  expect(cardTextFile({ en }, 'zh')).toBe(en);
  expect(cardTextFile({ en, zh: '../bad' }, 'zh')).toBeNull();
  expect(cardTextFile(undefined, 'en')).toBeNull();
});
it('reads the catalog from the same-origin Card Art proxy', async () => {
  const cards = { sample: { title: 'Radar Module' } };
  const fetch = vi.fn().mockResolvedValue(Response.json({ version: 1, cards }));
  vi.stubGlobal('fetch', fetch);
  const signal = new AbortController().signal;
  expect(await loadCardText(en, signal)).toEqual(cards);
  expect(fetch).toHaveBeenCalledWith('/card-art/' + en, { signal });
});
