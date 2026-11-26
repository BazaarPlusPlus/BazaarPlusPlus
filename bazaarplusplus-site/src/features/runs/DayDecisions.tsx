import { useMemo } from 'react';
import {
  presentMoment,
  presentStep,
  presentVisit,
  replayVisits,
  type MomentIndex,
  type VisitLabel
} from '@bpp/runs/decisions';
import {
  MomentPanel,
  RunReplay,
  Badge,
  FrameInspector,
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

/** The whole Run replayed step by step; the panel shows the board after the selected step. */
export function SiteReplay({
  visits,
  index,
  battles,
  art,
  names,
  locale
}: {
  visits: readonly Visit[];
  index: MomentIndex;
  battles: ReadonlyMap<string, RunBattle>;
  art: ArtLookup;
  names: Names;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  const labels = copy.path;
  const open = useOpenCard();
  const replay = useMemo(() => replayVisits(visits, index), [visits, index]);
  const views = useMemo(() => {
    const display = (c: CardRef) => pathCard(c, art, names, locale, open);
    const text = { ...labels, ...labels.kinds, ...labels.verbs };
    const label = (key: VisitLabel, params?: Record<string, string | number>) =>
      params ? fillCopy(text[key], params) : text[key];
    const result = (value: string) => {
      const enchant = enchantments.find((name) => name === value);
      return enchant ? (
        <EnchantLabel enchant={enchant} label={copy.enchantments[enchant]} />
      ) : (
        (copy.tiers[value] ?? value)
      );
    };
    return replay.map(({ visit, steps }) => {
      const battle = battles.get(visit.battleId ?? '');
      const view = presentVisit(
        visit,
        battle && {
          ...battle,
          opponentHero: battle.opponentHero
            ? heroName(battle.opponentHero)
            : null
        },
        display,
        label
      );
      return {
        ...view,
        key: visit.battleId ?? visit.order,
        day: visit.day === null ? null : fillCopy(copy.day, { day: visit.day }),
        outcome: battle?.outcome && (
          <Badge tone={battle.outcome === 'win' ? 'ok' : 'bad'}>
            {copy[battle.outcome]}
          </Badge>
        ),
        steps: steps.map((step) => {
          const row = presentStep(step, display, label);
          return {
            ...row,
            card: row.card && { card: row.card, title: row.title ?? '' },
            result: row.result && result(row.result)
          };
        })
      };
    });
  }, [replay, battles, art, names, locale, open, copy, labels]);
  return (
    <RunReplay
      visits={views}
      previous={labels.previous}
      next={labels.next}
      render={(v, s) => {
        const step = replay[v].steps[s];
        const view = views[v];
        const row = view.steps[s];
        const title = [view.day, view.hour, row.verb, row.card?.card.name]
          .filter(Boolean)
          .join(' · ');
        return step.moment ? (
          <MomentPanel
            formatPrice={(n) => fillCopy(labels.gold, { n })}
            labels={{ ...labels, skills: copy.skills }}
            title={title}
            reroll={step.verb === 'rerolled'}
            view={presentMoment(step.moment, (c) =>
              pathCard(c, art, names, locale, open)
            )}
          />
        ) : (
          <>
            <h3 className="bpp-moment-title">{title}</h3>
            <p>{labels.unknown}</p>
          </>
        );
      }}
    />
  );
}
