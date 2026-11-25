import type { CardRef, Context, DecisionEntry } from './project';

type Action = Extract<DecisionEntry, { kind: 'action' }>;
type Offer = Extract<DecisionEntry, { kind: 'offer' }>;
type Coordinates = Pick<Context, 'order' | 'day' | 'hour'>;

export type VisitKind =
  | 'shop'
  | 'event'
  | 'monster'
  | 'pvp'
  | 'pedestal'
  | 'encounter'
  | 'activity'
  | 'levelUp'
  | 'loot';

export type Visit = Coordinates & {
  kind: VisitKind;
  encounter: CardRef | null;
  actions: Action[];
  remaining: CardRef[];
  closed: boolean;
  battleId: string | null;
  gold: number | null;
  health: number | null;
  rerollFees: number[];
  gap: boolean;
};

function classify(state: string | null, card: CardRef | null): VisitKind {
  if (state === 'PVPCombat' || card?.type === 'PvpEncounter') return 'pvp';
  if (state === 'Combat' || card?.type === 'CombatEncounter') return 'monster';
  if (state === 'Pedestal' || card?.type === 'PedestalEncounter')
    return 'pedestal';
  if (card?.type === 'EventEncounter' && card.tag === 'Merchant') return 'shop';
  if (card?.tag === 'Event' && card.type === 'EventEncounter') return 'event';
  if (state === 'LevelUp') return 'levelUp';
  if (state === 'Loot' && !card) return 'loot';
  return card || state === 'Encounter' ? 'encounter' : 'activity';
}

function selector(state: string | null) {
  return (
    state === 'Choice' || state === 'LevelUp' || state === 'TournamentPvpChoice'
  );
}

function destination(c: Context, a: Action) {
  return (
    (!selector(c.state) &&
      c.encounter?.templateId &&
      c.encounter.templateId === a.card?.templateId) ||
    (c.state === 'Pedestal' && a.card?.type === 'PedestalEncounter') ||
    (c.state === 'PVPCombat' && a.card?.type === 'PvpEncounter') ||
    (c.state === 'Combat' && a.card?.type === 'CombatEncounter')
  );
}

/** Merge command-time entries and message-time boundaries without attributing activity across gaps. */
export function projectVisits(
  entries: readonly DecisionEntry[],
  contexts: readonly Context[]
): Visit[] {
  const visits: Visit[] = [];
  let context: Context | null = null;
  const cursor: { active: Visit | null } = { active: null };
  let offer: Offer | null = null;
  let pending: { action: Action; origin: Visit; index: number } | null = null;
  let known = true;
  let preceding: Context | null = null;
  let replaySeq: number | null = null;

  function open(
    at: Coordinates,
    kind: VisitKind,
    encounter: CardRef | null
  ): Visit {
    const visit: Visit = {
      order: at.order,
      day: known ? at.day : null,
      hour: known ? at.hour : null,
      kind,
      encounter,
      actions: [],
      remaining: [],
      closed: false,
      battleId: null,
      gold: 0,
      health: 0,
      rerollFees: [],
      gap: false
    };
    visits.push(visit);
    cursor.active = visit;
    return visit;
  }
  function close(complete: boolean) {
    if (cursor.active) cursor.active.closed = complete;
    cursor.active = null;
    offer = null;
  }
  function record(visit: Visit, action: Action, index = visit.actions.length) {
    visit.actions.splice(index, 0, action);
    for (const key of ['health', 'gold'] as const) {
      const delta = action[key];
      visit[key] =
        visit[key] === null || delta === null ? null : visit[key] + delta;
    }
  }
  function keepSelection() {
    if (pending) record(pending.origin, pending.action, pending.index);
    pending = null;
  }
  for (const e of [...contexts, ...entries].sort((a, b) => a.order - b.order)) {
    if (e.kind === 'context') {
      if (e.sync) {
        const previous = context ?? preceding;
        replaySeq =
          previous &&
          previous.state === e.state &&
          previous.day === e.day &&
          previous.hour === e.hour &&
          previous.encounter?.templateId === e.encounter?.templateId
            ? e.order
            : null;
        preceding = null;
        close(false);
        keepSelection();
        known = true;
      }
      if (!known) continue;
      const selected =
        pending &&
        pending.action.frames.includes(e.order) &&
        destination(e, pending.action)
          ? pending.action
          : null;
      const sameHour = context?.day === e.day && context.hour === e.hour;
      const inherited =
        sameHour && e.state === 'Loot' && cursor.active?.kind === 'monster'
          ? cursor.active.encounter
          : null;
      const parent = selected?.card ?? e.encounter ?? inherited;
      const kind = classify(e.state, parent);
      const sameParent =
        !!parent?.templateId &&
        cursor.active?.encounter?.templateId === parent.templateId;
      const changed =
        !sameHour ||
        (!sameParent &&
          (context?.encounter?.templateId !== parent?.templateId ||
            context?.state !== e.state));
      if (changed || selected) close(true);
      context = e;
      if (
        selected ||
        (e.order !== replaySeq &&
          !selector(e.state) &&
          (parent ||
            ['Combat', 'PVPCombat', 'Pedestal'].includes(e.state ?? '')))
      ) {
        const visit = cursor.active ?? open(e, kind, parent);
        if (selected) {
          record(visit, selected);
          pending = null;
        }
      }
      continue;
    }
    if (
      e.kind === 'undecoded' ||
      (e.kind === 'action' && e.verb === 'reconnect')
    ) {
      close(false);
      keepSelection();
      preceding = e.kind === 'action' ? context : null;
      context = null;
      if (e.kind === 'action') record(open(e, 'activity', null), e);
      known = false;
      cursor.active = null;
      if (e.kind === 'undecoded') open(e, 'activity', null).gap = true;
      continue;
    }
    if (e.kind === 'battle') {
      const kind = e.pvp ? 'pvp' : 'monster';
      const match =
        cursor.active?.kind === kind &&
        !cursor.active.battleId &&
        cursor.active.day === e.day &&
        cursor.active.hour === e.hour &&
        (!cursor.active.encounter ||
          !e.encounterId ||
          cursor.active.encounter.templateId === e.encounterId.toLowerCase());
      if (match && cursor.active) cursor.active.battleId = e.battleId;
      else {
        const prior: Visit | null = cursor.active;
        const fight = open(e, kind, null);
        fight.day = e.day;
        fight.hour = e.hour;
        fight.battleId = e.battleId;
        cursor.active = prior;
      }
      continue;
    }
    if (e.kind === 'offer') {
      if (!known) continue;
      offer = e;
      if (!cursor.active && !selector(e.state) && replaySeq !== e.frames[0]) {
        open(e, classify(e.state, e.encounter), e.encounter);
      }
      if (cursor.active && !selector(e.state)) {
        const seen = new Set(cursor.active.remaining.map((c) => c.instanceId));
        cursor.active.remaining.push(
          ...e.cards.filter((c) => !seen.has(c.instanceId))
        );
      }
      continue;
    }
    if (
      e.kind === 'reconnect' ||
      (e.kind === 'update' && !cursor.active && !e.gold && !e.health)
    )
      continue;
    const existing = cursor.active;
    const visit: Visit =
      existing ??
      open(
        e,
        classify(context?.state ?? null, context?.encounter ?? null),
        context?.encounter ?? null
      );
    if (!existing && offer && !selector(offer.state))
      visit.remaining.push(...offer.cards);
    if (e.kind === 'update') {
      visit.gold =
        visit.gold === null || e.gold === null ? null : visit.gold + e.gold;
      visit.health =
        visit.health === null || e.health === null
          ? null
          : visit.health + e.health;
      continue;
    }
    if (e.verb === 'chose') keepSelection();
    if (
      known &&
      selector(context?.state ?? null) &&
      e.verb === 'chose' &&
      e.outcome === 'done' &&
      offer?.cards.some((c) => c.instanceId === e.card?.instanceId) &&
      e.card?.type !== 'EncounterStep'
    ) {
      pending = { action: e, origin: visit, index: visit.actions.length };
      continue;
    }
    record(visit, e);
    if (e.outcome !== 'done') continue;
    if (e.verb === 'rerolled' && offer?.rerollCost != null)
      visit.rerollFees.push(offer.rerollCost);
    if (
      ['chose', 'picked', 'bought', 'skill'].includes(e.verb) &&
      offer?.cards.some((c) => c.instanceId === e.card?.instanceId)
    )
      visit.remaining = visit.remaining.filter(
        (c) => c.instanceId !== e.card?.instanceId
      );
    if (e.verb === 'left') close(true);
  }
  keepSelection();
  return visits.filter(
    (v) =>
      v.encounter ||
      v.actions.length ||
      v.battleId ||
      v.gap ||
      v.remaining.length ||
      v.gold ||
      v.health ||
      ['pedestal', 'monster', 'pvp'].includes(v.kind)
  );
}
