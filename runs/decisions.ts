import type { Moment, CardRef, BattleMarker } from './message-stream/project';
import type { Visit } from './message-stream/visits';
export type { Visit } from './message-stream/visits';
export type { CardRef, BattleMarker } from './message-stream/project';
export type { StreamFrame, MessageStream } from './message-stream/stream';

export function decisionRows(visit: Visit, active?: number, opening?: number) {
  const moves = visit.actions.filter(
    (a) => a.verb === 'moved' && a.outcome === 'done'
  );
  const rerolls = visit.actions.filter(
    (a) => a.verb === 'rerolled' && a.outcome === 'done'
  );
  return visit.actions.flatMap((a) => {
    let group: 'moves' | 'rerolls' | null = null;
    let count = 1;
    let amount = a.amount;
    let card = a.card;
    if (a.outcome === 'done') {
      if (
        a.verb === 'left' ||
        (a.verb === 'chose' &&
          visit.encounter &&
          opening === undefined &&
          visit.encounter?.instanceId === a.card?.instanceId)
      )
        return [];
      if (a.verb === 'moved' && !moves.some((m) => m.order === active)) {
        if (a !== moves[0]) return [];
        group = 'moves';
        count = moves.length;
        card = null;
      }
      if (a.verb === 'rerolled' && !rerolls.some((m) => m.order === active)) {
        if (a !== rerolls[0]) return [];
        group = 'rerolls';
        count = rerolls.length;
        amount =
          visit.rerollFees.length === rerolls.length
            ? -visit.rerollFees.reduce((sum, fee) => sum + fee, 0)
            : null;
      }
    }
    return [{ ...a, card, amount, group, count }];
  });
}

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
  | 'rerolls'
  | 'visited'
  | 'fought'
  | 'hour'
  | 'undecoded'
  | 'gold'
  | 'net'
  | 'health'
  | 'attempted'
  | 'noResponse'
  | 'notTaken'
  | 'offered';

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
  ) => string,
  active?: number,
  opening?: number
) {
  const encounter = visit.encounter
    ? display(visit.encounter)
    : battle?.encounterId
      ? display({
          templateId: battle.encounterId,
          instanceId: battle.encounterId,
          tier: null,
          enchant: null,
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
  const chip = (c: CardRef) => ({
    card: display(c),
    title: c.templateId ?? c.instanceId,
    id: c.instanceId
  });
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
    gap: visit.gap ? label('undecoded') : null,
    headerSeq:
      opening ??
      visit.actions.find(
        (a) =>
          a.verb === 'chose' &&
          a.outcome === 'done' &&
          visit.encounter &&
          a.card?.instanceId === visit.encounter.instanceId
      )?.order,
    rows: decisionRows(visit, active, opening).map((a) => ({
      id: a.order,
      verb: a.group ? label(a.group, { n: a.count }) : label(a.verb),
      chip: a.card ? chip(a.card) : null,
      result: a.result,
      status:
        a.outcome === 'done'
          ? null
          : label(a.outcome === 'no-response' ? 'noResponse' : 'attempted'),
      amount: a.amount
        ? label('gold', { n: a.amount < 0 ? '−' + -a.amount : a.amount })
        : null
    })),
    remaining: visit.remaining.map(chip),
    remainingLabel: label(visit.closed ? 'notTaken' : 'offered', {
      n: visit.remaining.length
    })
  };
}

/** Both hosts keep their battle bodies. Day cards include visit-only days and put unknown coordinates last. */
export function decisionDays<B>(
  battles: readonly B[],
  dayOf: (battle: B) => number | null,
  visits: readonly Visit[]
) {
  const days = [
    ...new Set([...battles.map(dayOf), ...visits.map((v) => v.day)])
  ].sort((a, b) => (a ?? Infinity) - (b ?? Infinity));
  return days.map((day) => ({
    day,
    battles: battles.filter((b) => dayOf(b) === day),
    visits: visits.filter((v) => v.day === day)
  }));
}

export type { Moment };
export type MomentIndex = {
  moments: Map<number, Moment>;
  openings: Map<string, Moment>;
};

export type DayMoment = { moment: Moment; verb: VisitLabel; visit: Visit };
export function dayMoments(
  visits: Visit[],
  index: MomentIndex | null | undefined
) {
  const rows = new Map<number, DayMoment>();
  if (index)
    for (const visit of visits) {
      for (const a of visit.actions) {
        if (a.verb === 'left' && a.outcome === 'done') continue;
        const moment = index.moments.get(a.order);
        if (moment) rows.set(a.order, { moment, verb: a.verb, visit });
      }
      const opening = visit.battleId && index.openings.get(visit.battleId);
      if (opening && opening.responseSeq !== null)
        rows.set(opening.responseSeq, {
          moment: opening,
          verb: visit.kind,
          visit
        });
    }
  return new Map([...rows].sort(([a], [b]) => a - b));
}

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
