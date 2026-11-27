import type { Moment, CardRef, BattleMarker } from './message-stream/project';
import type { Visit } from './message-stream/visits';
export type { Visit } from './message-stream/visits';
export type { CardRef, BattleMarker } from './message-stream/project';
export type { StreamFrame, MessageStream } from './message-stream/stream';

type Fight = {
  day: number | null;
  hour: number | null;
  encounterId: string | null;
  pvp: boolean;
};
export type EventBattle = Fight & {
  id: string;
  recordedAtMs: number;
  seq: number;
  outcome: 'win' | 'loss' | null;
};

export function combatFacts(
  captured: readonly (Fight & { id: string; recordedAtMs: number })[],
  events: readonly {
    seq: number;
    timestamp_ms: number;
    kind: string;
    body: string;
  }[]
) {
  const eventBattles: EventBattle[] = [];
  const battleSeqs = new Map<string, number>();
  for (const event of events) {
    if (
      event.kind !== 'pve_combat_recorded' &&
      event.kind !== 'pvp_combat_recorded'
    )
      continue;
    let body: Record<string, unknown>;
    try {
      body = JSON.parse(event.body);
    } catch {
      continue;
    }
    if (!body) continue;
    if (event.kind === 'pvp_combat_recorded') {
      if (typeof body.battle_id === 'string')
        battleSeqs.set(body.battle_id, event.seq);
      continue;
    }
    const day =
      typeof body.day === 'number' && Number.isSafeInteger(body.day)
        ? body.day
        : null;
    const hour =
      typeof body.hour === 'number' && Number.isSafeInteger(body.hour)
        ? body.hour
        : null;
    const encounterId =
      typeof body.encounter_id === 'string' ? body.encounter_id : null;
    if (
      captured.some(
        (b) =>
          !b.pvp &&
          b.day === day &&
          b.hour === hour &&
          (!b.encounterId || !encounterId || b.encounterId === encounterId)
      )
    )
      continue;
    eventBattles.push({
      id: 'event-' + event.seq,
      seq: event.seq,
      recordedAtMs: event.timestamp_ms,
      pvp: false,
      day,
      hour,
      encounterId,
      outcome:
        body.winner === 'Player'
          ? 'win'
          : body.loser === 'Player'
            ? 'loss'
            : null
    });
  }
  const markers: BattleMarker[] = captured.map(
    ({ id, recordedAtMs, day, hour, pvp, encounterId }) => ({
      id,
      recordedAtMs,
      day,
      hour,
      pvp,
      encounterId,
      seq: battleSeqs.get(id) ?? null
    })
  );
  markers.push(...eventBattles);
  markers.sort(
    (a, b) => a.recordedAtMs - b.recordedAtMs || a.id.localeCompare(b.id)
  );
  return { markers, eventBattles };
}

type Verb = Visit['actions'][number]['verb'];
export type VisitLabel =
  | Visit['kind']
  | Verb
  | 'moves'
  | 'visited'
  | 'fought'
  | 'hour'
  | 'undecoded'
  | 'gold'
  | 'net'
  | 'health'
  | 'attempted'
  | 'noResponse';

/** A visit's heading in the replay: what it was, where, and its net gold and health. */
export function presentVisit<C extends { name: string }>(
  visit: Visit,
  battle:
    | {
        encounterId: string | null;
        opponentHero: string | null;
        outcome: string | null;
      }
    | undefined,
  display: (c: CardRef) => C,
  label: (
    key: VisitLabel,
    params?: { n?: number | string; name?: string }
  ) => string
) {
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
      ? label('pvp') + ' · ' + battle.opponentHero
      : encounter && visit.kind !== 'levelUp' && visit.kind !== 'pvp'
        ? label(visit.kind === 'monster' ? 'fought' : 'visited', {
            name: encounter.name
          })
        : label(visit.kind);
  const delta = (value: number | null, key: 'net' | 'health') =>
    value
      ? {
          positive: value > 0,
          text: label(key, { n: value > 0 ? '+' + value : '−' + -value })
        }
      : null;
  return {
    title,
    encounter,
    hour: visit.hour === null ? null : label('hour', { n: visit.hour }),
    gold: delta(visit.gold, 'net'),
    health: delta(visit.health, 'health'),
    gap: visit.gap ? label('undecoded') : null
  };
}

export type ReplayStep = {
  /** The action's command seq, or the opening response seq for a battle step. */
  seq: number;
  verb: VisitLabel;
  /** Consecutive moves read as one step that ends on the last move. */
  count: number;
  card: CardRef | null;
  result: string | null;
  amount: number | null;
  status: 'attempted' | 'noResponse' | null;
  moment: Moment | null;
};
export type ReplayVisit = { visit: Visit; steps: ReplayStep[] };

/** Every visit in stream order with its steps; a visit without steps keeps its heading. */
export function replayVisits(
  visits: readonly Visit[],
  index: MomentIndex | null | undefined
): ReplayVisit[] {
  return visits.map((visit) => {
    const rerolls = visit.actions.filter(
      (a) => a.verb === 'rerolled' && a.outcome === 'done'
    );
    // A fee belongs to a reroll only when every reroll has one.
    const fees = visit.rerollFees.length === rerolls.length;
    const steps: ReplayStep[] = [];
    for (const a of visit.actions) {
      if (a.verb === 'left' && a.outcome === 'done') continue;
      const moment = index?.moments.get(a.order) ?? null;
      const last = steps.at(-1);
      if (
        a.verb === 'moved' &&
        a.outcome === 'done' &&
        last?.verb === 'moved' &&
        last.status === null
      ) {
        last.count += 1;
        last.card = null;
        last.seq = a.order;
        last.moment = moment;
        continue;
      }
      steps.push({
        seq: a.order,
        verb: a.verb,
        count: 1,
        card: a.card,
        result: a.result,
        amount:
          a.verb === 'rerolled' && a.outcome === 'done' && fees
            ? -visit.rerollFees[rerolls.indexOf(a)]
            : a.amount,
        status:
          a.outcome === 'done'
            ? null
            : a.outcome === 'no-response'
              ? 'noResponse'
              : 'attempted',
        moment
      });
    }
    const opening = visit.battleId && index?.openings.get(visit.battleId);
    if (opening && opening.responseSeq !== null) {
      const at = opening.responseSeq;
      const before = steps.findIndex((s) => s.seq > at);
      steps.splice(before < 0 ? steps.length : before, 0, {
        seq: at,
        verb: visit.kind,
        count: 1,
        card: null,
        result: null,
        amount: null,
        status: null,
        moment: opening
      });
    }
    return { visit, steps };
  });
}

/** A step's row: its verb, the card it acted on, the result and the gold it moved. */
export function presentStep<C>(
  step: ReplayStep,
  display: (c: CardRef) => C,
  label: (key: VisitLabel, params?: { n?: number | string }) => string
) {
  return {
    verb:
      step.verb === 'moved' && step.count > 1
        ? label('moves', { n: step.count })
        : label(step.verb),
    card: step.card ? display(step.card) : null,
    title: step.card ? (step.card.templateId ?? step.card.instanceId) : null,
    result: step.result,
    status: step.status && label(step.status),
    amount: step.amount
      ? label('gold', {
          n: step.amount < 0 ? '−' + -step.amount : step.amount
        })
      : null
  };
}

/** Battle days in day order; unknown days last. */
export function decisionDays<B>(
  battles: readonly B[],
  dayOf: (battle: B) => number | null
) {
  const days = [...new Set(battles.map(dayOf))].sort(
    (a, b) => (a ?? Infinity) - (b ?? Infinity)
  );
  return days.map((day) => ({
    day,
    battles: battles.filter((b) => dayOf(b) === day)
  }));
}

export type { Moment };
export type MomentIndex = {
  moments: Map<number, Moment>;
  openings: Map<string, Moment>;
};

export function presentMoment<C>(
  moment: Moment,
  display: (card: CardRef) => C
) {
  const placed = (cards: NonNullable<Moment['snapshot']>['board']) =>
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
