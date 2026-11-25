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
    cards: Record<string, CardDefinition>;
  };
  if (body.version !== 1 || !body.cards) throw new Error('Invalid Card Text');
  return { cards: body.cards, tagLabels: body.tagLabels ?? {} };
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
