import { useMemo } from 'react';
import {
  presentMoment,
  presentStep,
  presentVisit,
  replayVisits,
  type CardRef,
  type MessageStream,
  type MomentIndex,
  type Visit,
  type VisitLabel
} from '../../../runs/decisions';
import { Badge } from '../Badge';
import { EnchantLabel } from './Card';
import { useCardCatalog } from './catalog';
import { FrameInspector } from './FrameInspector';
import { MomentPanel, RunReplay } from './RunReplay';
import { fillTerm } from './terms';
import { cardTiers, enchantments } from './types';

/** What a Run's own records say about one fight; the opponent hero is a display name. */
export type ReplayBattle = {
  encounterId: string | null;
  opponentHero: string | null;
  outcome: 'win' | 'loss' | null;
};

/** The whole Run replayed step by step; the panel shows the board after the selected step. */
export function DecisionReplay({
  visits,
  index,
  battles,
  names
}: {
  visits: readonly Visit[];
  index: MomentIndex | undefined;
  battles: ReadonlyMap<string, ReplayBattle>;
  /** The Run's own name for a card, from its Battle snapshots, for a card the catalog lacks. */
  names?: (templateId: string) => string | null;
}) {
  const { card, terms } = useCardCatalog();
  const labels = terms.replay;
  const replay = useMemo(() => replayVisits(visits, index), [visits, index]);
  const display = useMemo(
    () => (c: CardRef) =>
      card({ ...c, name: c.templateId ? names?.(c.templateId) : null }),
    [card, names]
  );
  const views = useMemo(() => {
    const text: Record<VisitLabel, string> = {
      ...labels,
      ...labels.kinds,
      ...labels.verbs
    };
    const label = (key: VisitLabel, params?: Record<string, string | number>) =>
      params ? fillTerm(text[key], params) : text[key];
    const result = (value: string) => {
      const enchant = enchantments.find((name) => name === value);
      const tier = cardTiers.find((name) => name === value);
      return enchant ? (
        <EnchantLabel enchant={enchant} label={terms.enchantments[enchant]} />
      ) : tier ? (
        terms.tiers[tier]
      ) : (
        value
      );
    };
    return replay.map(({ visit, steps }) => {
      const battle = battles.get(visit.battleId ?? '');
      return {
        ...presentVisit(visit, battle, display, label),
        key: visit.battleId ?? visit.order,
        day: visit.day === null ? null : fillTerm(terms.day, { n: visit.day }),
        outcome: battle?.outcome && (
          <Badge tone={battle.outcome === 'win' ? 'ok' : 'bad'}>
            {terms[battle.outcome]}
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
  }, [replay, battles, display, terms, labels]);
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
            formatPrice={(n) => fillTerm(labels.gold, { n })}
            labels={{ ...labels, skills: terms.skills }}
            title={title}
            reroll={step.verb === 'rerolled'}
            view={presentMoment(step.moment, display)}
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

/** The Run Message Stream's raw frames, readable and downloadable. */
export function StreamFrames({ stream }: { stream: MessageStream }) {
  const { terms } = useCardCatalog();
  return (
    <FrameInspector
      frames={stream.frames}
      read={(frame) => stream.readableFrame(frame)}
      exportFrames={() => stream.exportFrames()}
      download={download}
      labels={terms.replay}
    />
  );
}
