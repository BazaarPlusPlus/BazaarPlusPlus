import { useQuery } from '@tanstack/react-query';

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
export function useCardArt(): (templateId: string) => string | null {
  const index = useCardArtIndex();
  return (templateId) => index.data?.files.get(templateId) ?? null;
}

/** A card's English title from the Card Art index; null until it loads or when unknown. */
export function useCardNames(): (templateId: string) => string | null {
  const index = useCardArtIndex();
  return (templateId) => index.data?.names.get(templateId) ?? null;
}
