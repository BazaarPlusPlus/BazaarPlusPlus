import { useMemo } from 'react';
import type { CardFx, FxMaterial } from '@bpp/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';

/** The site Worker serves Card Art same-origin from cdn.bazaarplusplus.com/card-art/. */
const CARD_ART_PATH = '/card-art/';
const FILE = /^[0-9a-f]{8,64}\.webp$/;

type CardArtIndex = {
  text?: Partial<Record<'en' | 'zh', unknown>>;
  fx: Map<string, string>;
  files: Map<string, string>;
  /** English titles from GameData, every card type; empty for an index from before them. */
  names: Map<string, string>;
};

export async function loadCardArtIndex(
  signal: AbortSignal
): Promise<CardArtIndex> {
  const response = await fetch(CARD_ART_PATH + 'index.json', { signal });
  if (!response.ok) throw new Error(`Card Art index ${response.status}`);
  const body = (await response.json()) as {
    cards?: unknown;
    fx?: unknown;
    names?: unknown;
    text?: CardArtIndex['text'];
  };
  if (!body.cards || typeof body.cards !== 'object') {
    throw new Error('Invalid Card Art index');
  }
  const files = new Map<string, string>();
  for (const [templateId, file] of Object.entries(body.cards)) {
    if (typeof file === 'string' && FILE.test(file)) {
      files.set(templateId.toLowerCase(), CARD_ART_PATH + file);
    }
  }
  const names = new Map<string, string>();
  if (body.names && typeof body.names === 'object') {
    for (const [templateId, name] of Object.entries(body.names)) {
      if (typeof name === 'string') names.set(templateId.toLowerCase(), name);
    }
  }
  const fx = new Map<string, string>();
  if (body.fx && typeof body.fx === 'object') {
    for (const [id, file] of Object.entries(body.fx))
      if (typeof file === 'string' && /^fx-[0-9a-f]{20}\.json$/.test(file))
        fx.set(id.toLowerCase(), CARD_ART_PATH + file);
  }
  return { files, names, fx, text: body.text };
}

export function useCardArtIndex() {
  return useQuery({
    queryKey: ['card-art-index'],
    queryFn: ({ signal }) => loadCardArtIndex(signal),
    staleTime: 60 * 60 * 1000,
    retry: false
  });
}

/** Card Art by TemplateId; null until the index loads, or when it fails. */
export type ArtLookup = ((templateId: string) => string | null) & {
  fx?: (
    templateId: string,
    tiny: boolean,
    slots?: number
  ) => CardFx | undefined;
};

export function useCardArt(): ArtLookup {
  const index = useCardArtIndex().data;
  const client = useQueryClient();
  return useMemo(() => {
    const sources = new Map<string, CardFx>();
    const art: ArtLookup = (id) => index?.files.get(id.toLowerCase()) ?? null;
    const fx = (id: string, tiny: boolean, slots?: number) => {
      const path = index?.fx.get(id.toLowerCase());
      if (!path) return;
      const key = path + String(tiny) + String(slots);
      if (!sources.has(key))
        sources.set(key, {
          tiny,
          slots,
          material: () =>
            client.fetchQuery({
              queryKey: ['card-material', path],
              staleTime: Infinity,
              retry: false,
              queryFn: async ({ signal }) => {
                const response = await fetch(path, { signal });
                if (!response.ok) throw new Error('Unavailable card material');
                return (await response.json()) as FxMaterial;
              }
            }),
          texture: (filename) =>
            client.fetchQuery({
              queryKey: ['card-texture', filename],
              staleTime: Infinity,
              retry: false,
              queryFn: async ({ signal }) => {
                if (
                  !FILE.test(filename) &&
                  !/^tex-[0-9a-f]{20}\.webp$/.test(filename)
                )
                  throw new Error('Invalid texture filename');
                const response = await fetch(CARD_ART_PATH + filename, {
                  signal
                });
                if (!response.ok) throw new Error('Unavailable card texture');
                return response.blob();
              }
            })
        });
      return sources.get(key);
    };
    return Object.assign(art, { fx });
  }, [index, client]);
}

/** A card's English title from the Card Art index; null until it loads or when unknown. */
export function useCardNames(): (templateId: string) => string | null {
  const index = useCardArtIndex();
  return (templateId) => index.data?.names.get(templateId) ?? null;
}
