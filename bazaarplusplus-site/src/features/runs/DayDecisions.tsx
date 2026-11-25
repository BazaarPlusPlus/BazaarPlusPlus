import { presentVisit } from '@bpp/runs/decisions';
import {
  Badge,
  FrameInspector,
  DecisionVisitRow,
  EnchantLabel,
  enchantments
} from '@bpp/ui';

import type { Locale } from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
import { heroName } from '../account/account-ui';
import type { RunBattle } from './run-payload';
import type { Visit } from '@bpp/runs/decisions';
import type { CardRef } from '@bpp/runs/decisions';
import { gameCard } from './game-card';
import { useOpenCard } from './CardDetails';
import type { MessageStream } from '@bpp/runs/decisions';

type ArtLookup = (templateId: string) => string | null;

/** A card's name by TemplateId, or null when none is known. */
type Names = (templateId: string) => string | null;

function cardName(card: CardRef, names: Names): string | null {
  return card.templateId ? names(card.templateId) : null;
}

/**
 * A card no name is known for: the PvP encounter (GameData has no title for it) reads PvP;
 * any other shows the start of its TemplateId, and the title has all of it.
 */
function shortId(card: CardRef, locale: Locale): string {
  if (card.type === 'PvpEncounter') return getSiteCopy(locale).account.pvp;
  return card.templateId?.slice(0, 8) ?? card.instanceId;
}

function pathCard(
  card: CardRef,
  art: ArtLookup,
  names: Names,
  locale: Locale,
  open: ReturnType<typeof useOpenCard>
) {
  return gameCard(
    card,
    cardName(card, names) ?? shortId(card, locale),
    art,
    locale,
    open
  );
}

function download(name: string, parts: Array<Uint8Array | string>) {
  const url = URL.createObjectURL(
    new Blob(parts as BlobPart[], {
      type:
        typeof parts[0] === 'string'
          ? 'application/json'
          : 'application/octet-stream'
    })
  );
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

export function StreamDebug({
  stream,
  locale
}: {
  stream: MessageStream;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account.path;
  return (
    <FrameInspector
      frames={stream.frames}
      read={(frame) => stream.readableFrame(frame)}
      exportFrames={() => stream.exportFrames()}
      download={download}
      labels={copy}
    />
  );
}

export function DecisionVisit({
  visit,
  battle,
  art,
  names,
  locale
}: {
  visit: Visit;
  battle?: RunBattle;
  art: ArtLookup;
  names: Names;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  const labels = copy.path;
  const open = useOpenCard();
  const display = (c: CardRef) => pathCard(c, art, names, locale, open);
  const outcome = battle?.outcome;
  const text = { ...labels, ...labels.kinds, ...labels.verbs };
  const view = presentVisit(
    visit,
    battle && {
      ...battle,
      opponentHero: battle.opponentHero ? heroName(battle.opponentHero) : null
    },
    display,
    (key, params) => (params ? fillCopy(text[key], params) : text[key])
  );
  return (
    <DecisionVisitRow
      view={view}
      outcome={
        outcome && (
          <Badge tone={outcome === 'win' ? 'ok' : 'bad'}>{copy[outcome]}</Badge>
        )
      }
      renderResult={(result) => {
        const enchant = enchantments.find((name) => name === result);
        return enchant ? (
          <EnchantLabel enchant={enchant} label={copy.enchantments[enchant]} />
        ) : (
          (copy.tiers[result] ?? result)
        );
      }}
    />
  );
}
