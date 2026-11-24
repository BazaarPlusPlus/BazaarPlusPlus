import type { CardSet, RunBattle, RunCard } from './run-payload';

const ITEM_TYPE = 0;
const TIERS = ['Bronze', 'Silver', 'Gold', 'Diamond', 'Legendary'];

export type TimelineCard = RunCard & {
  /** Set from the second observed board on. */
  change: 'new' | 'tier-up' | null;
  /** The tier on the previous observed board, for a tier-up. */
  previousTier: string | null;
};

export type CardDiff = {
  cards: TimelineCard[];
  /** On the previous observed board, gone from this one: sold, moved to the stash or consumed. */
  left: RunCard[];
};

export type TimelineStep = {
  battle: RunBattle;
  /** Null when the set was not recorded. */
  board: CardDiff | null;
  skills: CardDiff | null;
};

function tierRank(tier: string | null): number {
  return tier == null ? -1 : TIERS.indexOf(tier);
}

/** Matches cards by InstanceId, which survives tier-ups. */
export function diffCards(
  previous: readonly RunCard[] | null,
  current: readonly RunCard[]
): CardDiff {
  const before = new Map(previous?.map((c) => [c.instanceId, c]));
  const ids = new Set(current.map((c) => c.instanceId));
  return {
    cards: current.map((c) => {
      const prior = before.get(c.instanceId);
      const change =
        previous === null
          ? null
          : prior === undefined
            ? 'new'
            : tierRank(c.tier) > tierRank(prior.tier)
              ? 'tier-up'
              : null;
      return {
        ...c,
        change,
        previousTier: change === 'tier-up' ? (prior?.tier ?? null) : null
      };
    }),
    left: (previous ?? []).filter((c) => !ids.has(c.instanceId))
  };
}

function boardItems(set: CardSet): RunCard[] {
  return set.cards
    .filter((c) => c.type === ITEM_TYPE)
    .sort((a, b) => (a.slot ?? 0) - (b.slot ?? 0));
}

/**
 * One step per Battle. Each set is compared with the last observed one: a Missing set shows no
 * board and does not reset the comparison; CapturedEmpty is an observed empty board.
 */
export function buildTimeline(battles: readonly RunBattle[]): TimelineStep[] {
  let board: RunCard[] | null = null;
  let skills: RunCard[] | null = null;
  return battles.map((battle) => {
    const step: TimelineStep = { battle, board: null, skills: null };
    if (battle.hand.status !== 'missing') {
      const items = boardItems(battle.hand);
      step.board = diffCards(board, items);
      board = items;
    }
    if (battle.skills.status !== 'missing') {
      step.skills = diffCards(skills, battle.skills.cards);
      skills = battle.skills.cards;
    }
    return step;
  });
}
