// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  vi
} from 'vite-plus/test';
import {
  CardCatalog,
  CardTile,
  useCardCatalog,
  type CardInput,
  type GameLocale
} from '@bpp/ui';

const en = 'text-en-00000000000000000000.json';
const zh = 'text-zh-11111111111111111111.json';
const art = '0123456789abcdef0123.webp';
const shiny = { tags: [], lines: [] };
const heavy = { tags: ['Weapon'], lines: [] };

let container: HTMLDivElement;
let root: Root;
let files: Record<string, unknown>;
let fetched: string[];

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
    configurable: true,
    value: function (this: HTMLDialogElement) {
      this.open = true;
    }
  });
  Object.defineProperty(HTMLDialogElement.prototype, 'close', {
    configurable: true,
    value: function (this: HTMLDialogElement) {
      this.open = false;
    }
  });
  fetched = [];
  files = {
    'index.json': {
      version: 3,
      cards: { RADAR: art, BAD: '../escape.webp' },
      names: { RADAR: 'Radar Module', SWORD: 'Sword' },
      text: { en, zh }
    },
    [zh]: {
      version: 2,
      enchantments: [shiny, heavy],
      cards: {
        radar: {
          title: '雷达模块',
          size: 'Medium',
          tags: [],
          startingTier: 'Bronze',
          attributes: {},
          lines: [],
          enchantments: { Shiny: 0, Heavy: 1 }
        }
      },
      tagLabels: {}
    }
  };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      fetched.push(url);
      const name = url.slice(url.lastIndexOf('/') + 1);
      return name in files
        ? Response.json(files[name])
        : new Response(null, { status: 404 });
    })
  );
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  vi.unstubAllGlobals();
});

function Cards({ cards }: { cards: CardInput[] }) {
  const { card } = useCardCatalog();
  return cards.map((input, i) => <CardTile key={i} card={card(input)} />);
}

const input = (templateId: string | null, extra: Partial<CardInput> = {}) => ({
  templateId,
  tier: 'Silver',
  enchant: 'Shiny',
  ...extra
});

async function render(
  locale: GameLocale,
  revision: string,
  cards: CardInput[]
) {
  await act(async () =>
    root.render(
      <CardCatalog source={{ base: '/catalog/', revision }} locale={locale}>
        <Cards cards={cards} />
      </CardCatalog>
    )
  );
}

const names = () =>
  [...container.querySelectorAll<HTMLElement>('.bpp-card')].map(
    (n) => n.getAttribute('aria-label')?.split(' · ')[0]
  );

describe('CardCatalog', () => {
  it('names cards from the index in English, then from the Run, then by PvP or id', async () => {
    await render('en', 'en-names', [
      input('RADAR'),
      input('UNKNOWN-CARD-ID', { name: 'From snapshot' }),
      input('PVP-ENCOUNTER', { type: 'PvpEncounter' }),
      input('0123456789ab')
    ]);
    await vi.waitFor(() =>
      expect(names()).toEqual([
        'Radar Module',
        'From snapshot',
        'PvP',
        '01234567'
      ])
    );
    expect(container.querySelector('img')?.getAttribute('src')).toBe(
      '/catalog/' + art
    );
    expect(fetched).toEqual(['/catalog/index.json']);
  });

  it('names cards from the Chinese Card Text and opens the detail with its enchantments', async () => {
    await render('zh', 'zh-names', [input('RADAR'), input('SWORD')]);
    await vi.waitFor(() => expect(names()).toEqual(['雷达模块', 'Sword']));
    await act(async () =>
      container.querySelector<HTMLElement>('.bpp-card')!.click()
    );
    const dialog = document.querySelector('dialog')!;
    expect(dialog.querySelector('h2')?.textContent).toBe('雷达模块');
    expect(dialog.textContent).toContain('闪亮');
  });

  it('does not use a file name outside the catalog', async () => {
    await render('en', 'bad-name', [input('BAD')]);
    await vi.waitFor(() => expect(fetched).toContain('/catalog/index.json'));
    expect(container.querySelector('img')).toBeNull();
  });

  it('reads a new index when the revision changes', async () => {
    await render('en', 'first', [input('SWORD')]);
    await vi.waitFor(() => expect(names()).toEqual(['Sword']));
    files['index.json'] = { cards: {}, names: { SWORD: 'Renamed Sword' } };
    await render('en', 'second', [input('SWORD')]);
    await vi.waitFor(() => expect(names()).toEqual(['Renamed Sword']));
  });
});
