import { describe, expect, test } from 'vite-plus/test';

import type {
  CardSet,
  RunBattle,
  RunCard
} from '../src/features/runs/run-payload';
import { buildTimeline } from '../src/features/runs/run-timeline';

function item(id: string, tier = 'Bronze', slot = 0, type = 0): RunCard {
  return {
    instanceId: id,
    templateId: `template-${id}`,
    type,
    size: 1,
    slot,
    name: id,
    tier,
    enchant: null
  };
}

function captured(...cards: RunCard[]): CardSet {
  return { status: 'captured', cards };
}

const MISSING: CardSet = { status: 'missing', cards: [] };
const EMPTY: CardSet = { status: 'empty', cards: [] };

function battle(hand: CardSet, skills: CardSet = EMPTY): RunBattle {
  return {
    id: 'b',
    source: 'battle',
    recordedAtMs: 0,
    day: 1,
    hour: null,
    encounterId: null,
    pvp: true,
    outcome: 'win',
    opponentHero: null,
    hand,
    skills
  };
}

function changes(hand: CardSet[]) {
  return buildTimeline(hand.map((set) => battle(set))).map((step) =>
    step.board
      ? {
          cards: step.board.cards.map((c) =>
            [c.instanceId, c.change, c.previousTier].filter(Boolean).join(' ')
          ),
          left: step.board.left.map((c) => c.instanceId)
        }
      : null
  );
}

describe('board diff', () => {
  test('the first observed board carries no changes', () => {
    expect(changes([captured(item('a'), item('b', 'Silver', 1))])).toEqual([
      { cards: ['a', 'b'], left: [] }
    ]);
  });

  test('marks new items, items that left and tier-ups by InstanceId', () => {
    expect(
      changes([
        captured(item('a'), item('b', 'Bronze', 1)),
        captured(item('b', 'Silver', 1), item('c', 'Bronze', 2))
      ])[1]
    ).toEqual({ cards: ['b tier-up Bronze', 'c new'], left: ['a'] });
  });

  test('a Missing set shows no board and keeps the last observed one', () => {
    expect(
      changes([
        captured(item('a')),
        MISSING,
        captured(item('a', 'Gold'), item('b', 'Bronze', 1))
      ])
    ).toEqual([
      { cards: ['a'], left: [] },
      null,
      { cards: ['a tier-up Bronze', 'b new'], left: [] }
    ]);
  });

  test('CapturedEmpty is an observed empty board', () => {
    expect(changes([captured(item('a')), EMPTY, captured(item('a'))])).toEqual([
      { cards: ['a'], left: [] },
      { cards: [], left: ['a'] },
      { cards: ['a new'], left: [] }
    ]);
  });

  test('an item that leaves for the stash and returns is new again', () => {
    expect(
      changes([
        captured(item('a'), item('b', 'Bronze', 1)),
        captured(item('b', 'Bronze', 1)),
        captured(item('a', 'Bronze', 3), item('b', 'Bronze', 1))
      ]).map((step) => step && [step.cards, step.left])
    ).toEqual([
      [['a', 'b'], []],
      [['b'], ['a']],
      [['b', 'a new'], []]
    ]);
  });

  test('keeps only items on the board, in slot order', () => {
    expect(
      changes([
        captured(item('z', 'Gold', 5), item('s', 'Gold', 0, 7), item('y'))
      ])
    ).toEqual([{ cards: ['y', 'z'], left: [] }]);
  });

  test('skills are compared with the last observed skills', () => {
    const [first, second] = buildTimeline([
      battle(MISSING, captured(item('k'))),
      battle(MISSING, captured(item('k', 'Silver'), item('m')))
    ]);
    expect(first.board).toBeNull();
    expect(second.skills?.cards.map((c) => c.change)).toEqual([
      'tier-up',
      'new'
    ]);
  });
});
