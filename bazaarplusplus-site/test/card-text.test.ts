import { expect, it, vi, afterEach } from 'vite-plus/test';
import { cardTextFile, loadCardText } from '../src/features/runs/card-text';
import { loadCardArtIndex } from '../src/features/runs/card-art';
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
it('reads the catalog from the same-origin Card Art proxy and resolves shared enchantments', async () => {
  const shiny = { tags: [], lines: [] };
  const heavy = { tags: ['Weapon'], lines: [] };
  const tagLabels = { Tech: '科技' };
  const fetch = vi.fn().mockResolvedValue(
    Response.json({
      version: 2,
      enchantments: [shiny, heavy],
      cards: {
        radar: { title: 'Radar Module', enchantments: { Shiny: 0, Heavy: 1 } },
        sword: { title: 'Sword', enchantments: { Shiny: 0 } }
      },
      tagLabels
    })
  );
  vi.stubGlobal('fetch', fetch);
  const signal = new AbortController().signal;
  expect(await loadCardText(zh, signal)).toEqual({
    cards: {
      radar: {
        title: 'Radar Module',
        enchantments: { Shiny: shiny, Heavy: heavy }
      },
      sword: { title: 'Sword', enchantments: { Shiny: shiny } }
    },
    tagLabels
  });
  expect(fetch).toHaveBeenCalledWith('/card-art/' + zh, { signal });
});

it('refuses a card that names a missing enchantment', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(
      Response.json({
        version: 2,
        enchantments: [],
        cards: { radar: { title: 'Radar Module', enchantments: { Shiny: 0 } } }
      })
    )
  );
  await expect(loadCardText(zh, new AbortController().signal)).rejects.toThrow(
    'Invalid Card Text'
  );
});

it.each([undefined, 'fx-0123456789abcdef0123.json'])(
  'reads ordinary art and the optional material map',
  async (material) => {
    const file = '0123456789abcdef0123.webp';
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        Response.json({
          version: 3,
          cards: { ITEM: file },
          names: { ITEM: 'Example' },
          text: { en, zh },
          ...(material ? { fx: { ITEM: material } } : {})
        })
      )
    );
    const index = await loadCardArtIndex(new AbortController().signal);
    expect(index.files.get('item')).toBe('/card-art/' + file);
    expect(index.names.get('item')).toBe('Example');
    expect(index.text).toEqual({ en, zh });
    expect(index.fx.get('item')).toBe(material && '/card-art/' + material);
  }
);
