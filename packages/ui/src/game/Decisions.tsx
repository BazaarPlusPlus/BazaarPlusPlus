import { useMemo } from 'react';
import { Badge } from '../Badge';
import { EnchantLabel, type GameCard } from './Card';
import { useCardCatalog } from './catalog';
import { FrameInspector } from './FrameInspector';
import type {
  MessageStreamLike,
  ReplayCardRef,
  ReplayMoment,
  ReplayVisit,
  StreamFrameLike
} from './replay-types';
import { MomentPanel, RunReplay } from './RunReplay';
import { fillTerm } from './terms';
import { cardTiers, enchantments } from './types';

/** What a Run's own records say about one fight; the opponent hero is a display name. */
export type ReplayBattle = {
  encounterId: string | null;
  opponentHero: string | null;
  outcome: 'win' | 'loss' | null;
};

function presentMoment(
  moment: ReplayMoment,
  display: (card: ReplayCardRef) => GameCard
) {
  const placed = (cards: NonNullable<ReplayMoment['snapshot']>['board']) =>
    cards?.map((c) => ({
      ...display(c),
      id: c.instanceId,
      slot: c.slot,
      size: c.size
    })) ?? null;
  const s = moment.snapshot;
  return {
    board: placed(s?.board ?? null),
    stash: placed(s?.stash ?? null),
    skills:
      s?.skills?.map((c) => ({ ...display(c), id: c.instanceId })) ?? null,
    encounterId: moment.encounter?.instanceId ?? null,
    encounter: moment.encounter ? display(moment.encounter) : null,
    offer:
      moment.offer?.map((c) => ({
        card: display(c),
        id: c.instanceId,
        chosen: c.instanceId === moment.chosen,
        price: c.price
      })) ?? (moment.offer as null | undefined),
    gold: s?.gold ?? null,
    health: s?.health ?? null,
    level: s?.level ?? null
  };
}

/** The whole Run replayed step by step; the panel shows the board after the selected step. */
export function DecisionReplay({
  replay,
  battles,
  names
}: {
  replay: readonly ReplayVisit[];
  battles: ReadonlyMap<string, ReplayBattle>;
  /** The Run's own name for a card, from its Battle snapshots, for a card the catalog lacks. */
  names?: (templateId: string) => string | null;
}) {
  const { card, terms } = useCardCatalog();
  const labels = terms.replay;
  const display = useMemo(
    () => (c: ReplayCardRef) =>
      card({ ...c, name: c.templateId ? names?.(c.templateId) : null }),
    [card, names]
  );
  const views = useMemo(() => {
    const verbText = { ...labels.kinds, ...labels.verbs };
    const delta = (value: number | null, template: string) =>
      value
        ? {
            positive: value > 0,
            text: fillTerm(template, {
              n: value > 0 ? '+' + value : '−' + -value
            })
          }
        : null;
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
      const encounter = visit.encounter
        ? display(visit.encounter)
        : battle?.encounterId
          ? display({
              templateId: battle.encounterId,
              instanceId: battle.encounterId,
              tier: null,
              enchant: null,
              size: null,
              price: null,
              type: null,
              tag: null
            })
          : undefined;
      const title =
        visit.kind === 'pvp' && battle?.opponentHero
          ? labels.kinds.pvp + ' · ' + battle.opponentHero
          : encounter && visit.kind !== 'levelUp' && visit.kind !== 'pvp'
            ? fillTerm(
                visit.kind === 'monster' ? labels.fought : labels.visited,
                { name: encounter.name }
              )
            : labels.kinds[visit.kind];
      return {
        key: visit.battleId ?? visit.order,
        day: visit.day === null ? null : fillTerm(terms.day, { n: visit.day }),
        title,
        encounter,
        hour:
          visit.hour === null ? null : fillTerm(labels.hour, { n: visit.hour }),
        gold: delta(visit.gold, labels.net),
        health: delta(visit.health, labels.health),
        gap: visit.gap ? labels.undecoded : null,
        outcome: battle?.outcome && (
          <Badge tone={battle.outcome === 'win' ? 'ok' : 'bad'}>
            {terms[battle.outcome]}
          </Badge>
        ),
        steps: steps.map((step) => ({
          verb:
            step.verb === 'moved' && step.count > 1
              ? fillTerm(labels.moves, { n: step.count })
              : verbText[step.verb],
          card: step.card && {
            card: display(step.card),
            title: step.card.templateId ?? step.card.instanceId
          },
          result: step.result && result(step.result),
          status: step.status && labels[step.status],
          amount: step.amount
            ? fillTerm(labels.gold, {
                n: step.amount < 0 ? '−' + -step.amount : step.amount
              })
            : null
        }))
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

/** The Run Message Stream's raw frames, readable and downloadable. */
export function StreamFrames<F extends StreamFrameLike = StreamFrameLike>({
  stream
}: {
  stream: MessageStreamLike<F>;
}) {
  const { terms } = useCardCatalog();
  return (
    <FrameInspector
      frames={stream.frames}
      read={(frame) => stream.readableFrame(frame)}
      exportFrames={() => stream.exportFrames()}
      labels={terms.replay}
    />
  );
}
