import { useQuery } from '@tanstack/react-query';
import type { CardDefinition } from '@bpp/ui';
import type { Locale } from '../../app/router';
import { useCardArtIndex } from './card-art';

export function cardTextFile(
  index: Partial<Record<Locale, unknown>> | undefined,
  locale: Locale
) {
  const language = locale === 'zh' && index?.zh !== undefined ? 'zh' : 'en';
  const file = index?.[language];
  return typeof file === 'string' &&
    new RegExp(`^text-${language}-[0-9a-f]{20}\\.json$`).test(file)
    ? file
    : null;
}

export async function loadCardText(file: string, signal: AbortSignal) {
  const response = await fetch('/card-art/' + file, { signal });
  if (!response.ok) throw new Error(`Card Text ${response.status}`);
  const body = (await response.json()) as {
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
    const enchantments: CardDefinition['enchantments'] = {};
    for (const [name, position] of Object.entries(card.enchantments)) {
      const effect = body.enchantments[position];
      if (!effect) throw new Error('Invalid Card Text');
      enchantments[name] = effect;
    }
    cards[id] = { ...card, enchantments };
  }
  return { cards, tagLabels: body.tagLabels ?? {} };
}

export function useCardText(locale: Locale) {
  const index = useCardArtIndex();
  const file = cardTextFile(index.data?.text, locale);
  const query = useQuery({
    queryKey: ['card-text', file],
    queryFn: ({ signal }) => loadCardText(file!, signal),
    enabled: !!file,
    staleTime: Infinity,
    retry: false
  });
  return {
    cards: query.data?.cards,
    tagLabels: file?.startsWith(`text-${locale}-`)
      ? (query.data?.tagLabels ?? {})
      : {},
    loading: index.isPending || (!!file && query.isPending)
  };
}
