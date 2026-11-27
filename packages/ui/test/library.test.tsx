// @vitest-environment jsdom

import { act, StrictMode, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  vi
} from 'vite-plus/test';
import { Button, Dialog, StatusPanel } from '../src/index';
import {
  CardDetail,
  CardTile,
  CardText,
  type CardTextLine,
  SkillStrip
} from '../src/game/index';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  // jsdom has no modal dialog; model only the open flag the component reads.
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

/** Escape on a modal dialog fires `cancel`. */
function escape() {
  container
    .querySelector('dialog')
    ?.dispatchEvent(new Event('cancel', { cancelable: true }));
}

async function render(node: ReactNode) {
  await act(async () => root.render(<StrictMode>{node}</StrictMode>));
}

describe('Button', () => {
  it('blocks a busy button and names it busy', () => {
    const html = renderToStaticMarkup(<Button busy>Install</Button>);
    expect(html).toContain('disabled=""');
    expect(html).toContain('aria-busy="true"');
    expect(html).toContain('type="button"');
  });

  it('keeps a disabled link out of the tab order and ignores its clicks', async () => {
    const onClick = vi.fn();
    await render(
      <Button href="https://example.com" disabled onClick={onClick}>
        Open
      </Button>
    );
    const link = container.querySelector('a');
    expect(link?.getAttribute('aria-disabled')).toBe('true');
    expect(link?.getAttribute('tabindex')).toBe('-1');
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    await act(async () => link?.dispatchEvent(event));
    expect(onClick).not.toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(true);
  });
});

describe('StatusPanel', () => {
  it('titles the panel at the requested level, a page title at level 1', () => {
    expect(
      renderToStaticMarkup(<StatusPanel kind="empty" title="Nothing" />)
    ).toContain('<h2 class="bpp-status-panel-title">Nothing</h2>');
    expect(
      renderToStaticMarkup(
        <StatusPanel kind="not-found" title="Lost" headingLevel={1} />
      )
    ).toContain('<h1 class="bpp-status-panel-title is-page">Lost</h1>');
  });

  it('names the loading mark as a progress bar when given a label', () => {
    const html = renderToStaticMarkup(
      <StatusPanel
        kind="loading"
        title="Loading"
        progressLabel="Loading data"
      />
    );
    expect(html).toContain('role="progressbar"');
    expect(html).toContain('aria-label="Loading data"');
  });
});

describe('Dialog', () => {
  it('locks page scroll while open and returns focus to its opener', async () => {
    const opener = document.createElement('button');
    document.body.append(opener);
    opener.focus();

    await render(
      <Dialog onClose={() => undefined} labelledBy="t">
        <button type="button">Inside</button>
      </Dialog>
    );
    expect(container.querySelector('dialog')?.open).toBe(true);
    expect(document.body.style.overflow).toBe('hidden');
    container.querySelector<HTMLButtonElement>('dialog button')?.focus();

    await act(async () => root.render(<StrictMode>{null}</StrictMode>));
    expect(document.body.style.overflow).toBe('');
    expect(document.activeElement).toBe(opener);
    opener.remove();
  });

  it('closes on Escape unless dismissal is off', async () => {
    const onClose = vi.fn();

    await render(
      <Dialog onClose={onClose} dismissible={false}>
        <p>Busy</p>
      </Dialog>
    );
    await act(async () => escape());
    expect(onClose).not.toHaveBeenCalled();

    await render(
      <Dialog onClose={onClose}>
        <p>Idle</p>
      </Dialog>
    );
    await act(async () => escape());
    expect(onClose).toHaveBeenCalledOnce();
  });
});

function lineText(value: CardTextLine) {
  container.innerHTML = renderToStaticMarkup(
    <CardText line={value} unavailable="Unavailable" />
  );
  return container.textContent;
}

describe('Card Text', () => {
  const line: CardTextLine = {
    id: 0,
    available: true,
    resolved: 'full',
    tiers: ['Silver', 'Gold', 'Diamond'],
    segments: [
      {
        token: '{ability.0}',
        tiers: { Silver: 4, Gold: 8, Diamond: 12 },
        unit: 'percent'
      },
      { text: '% ' },
      { keyword: 'Crit Chance', label: '暴击率' }
    ]
  };
  it('keeps percent signs, localized keywords and the captured tier', async () => {
    await render(
      <CardText line={line} tier="Gold" unavailable="Unavailable" />
    );
    expect(container.textContent).toBe('4% » 8% » 12% 暴击率');
    expect(container.querySelector('strong.bpp-tier-gold')?.textContent).toBe(
      '8%'
    );
    expect(
      container.querySelector('.bpp-keyword-crit-chance')?.textContent
    ).toBe('暴击率');
  });
  it('marks missing values in place across partial and unresolved lines', () => {
    const partial: CardTextLine = {
      ...line,
      resolved: 'partial',
      segments: [{ token: '{aura.9}', tiers: { Gold: 1 } }]
    };
    expect(lineText(partial)).toBe('Unavailable » 1 » Unavailable');
    expect(
      lineText({
        ...line,
        resolved: 'none',
        segments: [{ text: 'Duration ' }, { token: '{aura.9}' }]
      })
    ).toBe('Duration Unavailable');
    expect(
      lineText({ ...line, available: false, resolved: 'none', segments: [] })
    ).toBe('Unavailable');
  });
});

const tags = (selector: string) =>
  [...container.querySelectorAll(`${selector} .bpp-detail-tags li`)].map(
    (tag) => tag.textContent
  );

it('renders only localized card and enchantment tags', async () => {
  await render(
    <CardDetail
      card={{
        name: '雷达',
        art: null,
        tier: null,
        tierLabel: '',
        enchant: 'Obsidian',
        enchantLabel: '黑曜石'
      }}
      definition={{
        title: '雷达',
        size: 'Medium',
        startingTier: 'Silver',
        attributes: {},
        tags: ['Tech', 'Relic'],
        lines: [],
        enchantments: { Obsidian: { tags: ['Weapon', 'Relic'], lines: [] } }
      }}
      tagLabels={{ Tech: '科技', Weapon: '武器' }}
      labels={{
        sizes: { Medium: '中型' },
        enchantments: '附魔',
        allEnchantments: '',
        cooldown: '',
        ammo: '',
        seconds: '',
        unavailable: ''
      }}
      tierLabels={{}}
      enchantLabels={{
        Deadly: '',
        Ethereal: '',
        Fiery: '',
        Golden: '',
        Heavy: '',
        Icy: '',
        Mystical: '',
        Obsidian: '黑曜石',
        Radiant: '',
        Restorative: '',
        Shielded: '',
        Shiny: '',
        Tiny: '',
        Toxic: '',
        Turbo: '',
        Mossy: ''
      }}
    />
  );
  expect(tags('header')).toEqual(['中型', '科技']);
  expect(tags('.bpp-detail-enchant')).toEqual(['武器']);
});

describe('CardTile', () => {
  it('keeps the accessible enchantment and change marker without a material', () => {
    const html = renderToStaticMarkup(
      <CardTile
        card={{
          name: 'Tambourine',
          art: 'badge.webp',
          tier: 'Silver',
          tierLabel: 'Silver',
          enchant: 'Shiny',
          enchantLabel: 'Shiny',
          change: 'tier-up'
        }}
      />
    );
    expect(html).toContain('title="Tambourine · Silver · Shiny"');
    expect(html).toContain('data-change="tier-up"');
    expect(html).toContain('>Shiny</span>');
    expect(html).not.toContain('<canvas');
  });

  it('leaves a plain skill on its illustration', () => {
    const html = renderToStaticMarkup(
      <SkillStrip
        cards={[
          {
            id: 'keen-eye',
            name: 'Keen Eye',
            art: 'skill.webp',
            tier: null,
            tierLabel: '',
            enchant: null,
            enchantLabel: ''
          }
        ]}
        label="Skills"
        emptyLabel=""
        missingLabel=""
      />
    );
    expect(html).toContain('skill.webp');
    expect(html).not.toContain('<canvas');
  });
});
