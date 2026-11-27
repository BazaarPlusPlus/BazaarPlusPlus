import {
  createContext,
  use,
  useCallback,
  useMemo,
  useState,
  useSyncExternalStore,
  type ComponentType,
  type ReactNode
} from 'react';
import { Button } from '../Button';
import { Dialog, DialogBody, DialogCard, DialogFooter } from '../Dialog';
import type { GameCard } from './Card';
import { CardDetail, type CardDefinition } from './CardDetail';
import type { CardFx, FxMaterial } from './enchantment-fx/program';
import { gameTerms, type GameLocale, type GameTerms } from './terms';
import { cardTiers, enchantments, type CardSize } from './types';

/**
 * Where the card catalog is read from: a directory holding `index.json` and the files it
 * names. Every named file is immutable; `revision` changes when `index.json` does.
 */
export type CardSource = { base: string; revision?: string };

/** A card as Runs record it; `name` is the product's own name for it, used when the catalog has none. */
export type CardInput = {
  templateId: string | null;
  instanceId?: string;
  type?: string | null;
  tier: string | null;
  enchant: string | null;
  size?: number | null;
  name?: string | null;
};

type Snapshot<T> = { data?: T; error?: unknown; pending: boolean };
type Entry = {
  snapshot: Snapshot<unknown>;
  listeners: Set<() => void>;
  load: () => Promise<unknown>;
  promise?: Promise<unknown>;
};

// One copy of each file per page, shared by every component that reads it.
const entries = new Map<string, Entry>();
const IDLE: Snapshot<never> = { pending: false };

function entry(key: string, load: () => Promise<unknown>) {
  let found = entries.get(key);
  if (!found) {
    found = { snapshot: { pending: true }, listeners: new Set(), load };
    entries.set(key, found);
  }
  return found;
}

function start(found: Entry) {
  const settle = (snapshot: Snapshot<unknown>) => {
    found.snapshot = snapshot;
    for (const notify of found.listeners) notify();
  };
  found.promise ??= found.load().then(
    (data) => {
      settle({ data, pending: false });
      return data;
    },
    (error: unknown) => {
      settle({ error, pending: false });
      throw error;
    }
  );
  return found.promise;
}

function request<T>(key: string, load: () => Promise<T>): Promise<T> {
  const found = entry(key, load);
  return (start(found) as Promise<T>).catch((error: unknown) => {
    // A failed request is tried again the next time something asks for it.
    if (entries.get(key) === found) entries.delete(key);
    throw error;
  });
}

function useResource<T>(
  key: string | null,
  load: () => Promise<T>
): Snapshot<T> {
  const found = key ? entry(key, load) : null;
  const subscribe = useCallback(
    (notify: () => void) => {
      if (!found) return () => {};
      found.listeners.add(notify);
      start(found).catch(() => {
        // The snapshot carries the error.
      });
      return () => {
        found.listeners.delete(notify);
        // Evict a failed fetch once unmounted so the next open retries (ADR 0025),
        // without triggering a re-fetch loop while still mounted.
        if (
          key &&
          found.listeners.size === 0 &&
          found.snapshot.error !== undefined &&
          entries.get(key) === found
        ) {
          entries.delete(key);
        }
      };
    },
    [found, key]
  );
  return useSyncExternalStore(
    subscribe,
    () => found?.snapshot ?? IDLE,
    () => found?.snapshot ?? IDLE
  ) as Snapshot<T>;
}

async function json(url: string) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url} ${response.status}`);
  return (await response.json()) as unknown;
}

const ART = /^[0-9a-f]{8,64}\.webp$/;
const TEXTURE = /^tex-[0-9a-f]{20}\.webp$/;
const MATERIAL = /^fx-[0-9a-f]{20}\.json$/;

type Index = {
  art: Map<string, string>;
  /** English titles from GameData, for every card type. */
  names: Map<string, string>;
  fx: Map<string, string>;
  text: Partial<Record<GameLocale, string>>;
};

function files(value: unknown, pattern: RegExp) {
  const out = new Map<string, string>();
  if (value && typeof value === 'object')
    for (const [id, file] of Object.entries(value))
      if (typeof file === 'string' && pattern.test(file))
        out.set(id.toLowerCase(), file);
  return out;
}

async function loadIndex(source: CardSource): Promise<Index> {
  const body = (await json(source.base + 'index.json')) as Record<
    string,
    unknown
  >;
  if (!body.cards || typeof body.cards !== 'object')
    throw new Error('Invalid card catalog index');
  const text: Index['text'] = {};
  const named = (body.text ?? {}) as Record<string, unknown>;
  for (const locale of ['en', 'zh'] as const) {
    const file = named[locale];
    if (
      typeof file === 'string' &&
      new RegExp(`^text-${locale}-[0-9a-f]{20}\\.json$`).test(file)
    )
      text[locale] = file;
  }
  return {
    art: files(body.cards, ART),
    names: files(body.names, /./),
    fx: files(body.fx, MATERIAL),
    text
  };
}

type CardText = {
  cards: Record<string, CardDefinition>;
  tagLabels: Record<string, string>;
};

async function loadText(url: string): Promise<CardText> {
  const body = (await json(url)) as {
    version: number;
    tagLabels?: Record<string, string>;
    // Each distinct enchantment definition is stored once; a card names it by position.
    enchantments: CardDefinition['enchantments'][string][];
    cards: Record<
      string,
      Omit<CardDefinition, 'enchantments'> & {
        enchantments: Record<string, number>;
      }
    >;
  };
  if (body.version !== 2 || !body.cards || !body.enchantments)
    throw new Error('Invalid Card Text');
  const cards: Record<string, CardDefinition> = {};
  for (const [id, card] of Object.entries(body.cards)) {
    const named: CardDefinition['enchantments'] = {};
    for (const [name, position] of Object.entries(card.enchantments)) {
      const effect = body.enchantments[position];
      if (!effect) throw new Error('Invalid Card Text');
      named[name] = effect;
    }
    cards[id] = { ...card, enchantments: named };
  }
  return { cards, tagLabels: body.tagLabels ?? {} };
}

function indexKey(source: CardSource) {
  return `index ${source.base} ${source.revision ?? ''}`;
}

function useIndex(source: CardSource | null) {
  return useResource(source && indexKey(source), () =>
    source ? loadIndex(source) : Promise.reject(new Error('No card source'))
  );
}

/** The Card Text for a locale; English when the catalog has none for it. */
function useCardText(source: CardSource | null, locale: GameLocale) {
  const index = useIndex(source);
  const language = index.data?.text[locale] ? locale : 'en';
  const file = index.data?.text[language];
  const url = source && file ? source.base + file : null;
  const text = useResource(url, () =>
    url ? loadText(url) : Promise.reject(new Error('No Card Text'))
  );
  return {
    cards: text.data?.cards,
    tagLabels: language === locale ? (text.data?.tagLabels ?? {}) : {},
    loading: index.pending || text.pending
  };
}

type Display = Omit<GameCard, 'preview' | 'onOpen'>;
type Selection = { templateId: string | null; card: Display };

type Catalog = {
  source: CardSource | null;
  locale: GameLocale;
  terms: GameTerms;
  /** A card's name in the catalog's locale; null when the catalog does not know it. */
  name: (templateId: string) => string | null;
  /** The card as the board, replay and detail show it, with its preview and dialog. */
  card: (input: CardInput) => GameCard;
};

const CatalogContext = createContext<Catalog | null>(null);

export function useCardCatalog(): Catalog {
  const catalog = use(CatalogContext);
  if (!catalog) throw new Error('useCardCatalog needs a CardCatalog');
  return catalog;
}

function Definition({
  templateId,
  card,
  titleId,
  preview = false
}: {
  templateId: string | null;
  card: Display;
  titleId?: string;
  preview?: boolean;
}) {
  const { source, locale, terms } = useCardCatalog();
  const { cards, tagLabels, loading } = useCardText(source, locale);
  return (
    <CardDetail
      card={card}
      definition={templateId ? cards?.[templateId.toLowerCase()] : undefined}
      tagLabels={tagLabels}
      labels={
        loading ? { ...terms.card, unavailable: terms.loading } : terms.card
      }
      tierLabels={terms.tiers}
      enchantLabels={terms.enchantments}
      titleId={titleId}
      preview={preview}
    />
  );
}

/** Shows the card dialog while `open`; a product with a modal stack replaces it. */
export type CardDialogHost = ComponentType<{
  open: boolean;
  children: ReactNode;
}>;

function PlainHost({ open, children }: { open: boolean; children: ReactNode }) {
  return open ? children : null;
}

/**
 * Card names, art, Enchantment FX and Card Text from one card catalog, for every card shown
 * inside it; clicking a card opens its detail dialog.
 */
export function CardCatalog({
  source,
  locale,
  dialogHost: Host = PlainHost,
  children
}: {
  source: CardSource | null;
  locale: GameLocale;
  dialogHost?: CardDialogHost;
  children: ReactNode;
}) {
  const index = useIndex(source).data;
  // Names in Chinese come from the Chinese Card Text, so it loads with the page.
  const titles = useCardText(locale === 'zh' ? source : null, 'zh').cards;
  const [selection, setSelection] = useState<Selection | null>(null);
  const terms = gameTerms[locale];
  const catalog = useMemo<Catalog>(() => {
    const materials = new Map<string, CardFx>();
    const name = (id: string) => {
      const key = id.toLowerCase();
      return titles?.[key]?.title ?? index?.names.get(key) ?? null;
    };
    const fx = (id: string, tiny: boolean, slots?: number) => {
      const file = index?.fx.get(id.toLowerCase());
      if (!source || !file) return;
      const key = `${file} ${tiny} ${slots}`;
      let found = materials.get(key);
      if (!found) {
        const base = source.base;
        found = {
          tiny,
          slots,
          material: () =>
            request(base + file, () =>
              json(base + file)
            ) as Promise<FxMaterial>,
          texture: async (filename) => {
            if (!ART.test(filename) && !TEXTURE.test(filename))
              throw new Error('Invalid texture filename');
            const response = await fetch(base + filename);
            if (!response.ok) throw new Error('Unavailable card texture');
            return response.blob();
          }
        };
        materials.set(key, found);
      }
      return found;
    };
    const card = (input: CardInput): GameCard => {
      const id = input.templateId;
      const tier = cardTiers.find((value) => value === input.tier) ?? null;
      const enchant =
        enchantments.find((value) => value === input.enchant) ?? null;
      const file = id ? index?.art.get(id.toLowerCase()) : undefined;
      const display: Display = {
        name:
          (id && name(id)) ||
          input.name ||
          (input.type === 'PvpEncounter'
            ? terms.pvp
            : (id?.slice(0, 8) ?? input.instanceId ?? '')),
        art: source && file ? source.base + file : null,
        fx:
          enchant && id
            ? fx(id, enchant === 'Tiny', input.size ?? undefined)
            : undefined,
        tier,
        tierLabel: input.tier ? (tier ? terms.tiers[tier] : input.tier) : '',
        enchant,
        enchantLabel: enchant
          ? terms.enchantments[enchant]
          : (input.enchant ?? ''),
        size:
          ([1, 2, 3] as const).find((n): n is CardSize => n === input.size) ??
          null
      };
      return {
        ...display,
        preview: id ? (
          <Definition templateId={id} card={display} preview />
        ) : undefined,
        onOpen: () => setSelection({ templateId: id, card: display })
      };
    };
    return { source, locale, terms, name, card };
  }, [source, locale, terms, index, titles]);
  const close = () => setSelection(null);
  return (
    <CatalogContext.Provider value={catalog}>
      {children}
      <Host open={!!selection}>
        {selection && (
          <Dialog labelledBy="card-detail-title" onClose={close}>
            <DialogCard>
              <DialogBody>
                <Definition
                  templateId={selection.templateId}
                  card={selection.card}
                  titleId="card-detail-title"
                />
              </DialogBody>
              <DialogFooter>
                <Button onClick={close}>{terms.card.close}</Button>
              </DialogFooter>
            </DialogCard>
          </Dialog>
        )}
      </Host>
    </CatalogContext.Provider>
  );
}
