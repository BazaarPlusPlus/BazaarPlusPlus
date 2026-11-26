import { expect, test } from 'vite-plus/test';
import { replayVisits, type MomentIndex, type Visit } from '../decisions';
import type { CardRef, Moment } from '../message-stream/project';

const card = (id: string): CardRef => ({
  templateId: id,
  instanceId: id,
  tier: null,
  enchant: null,
  size: null,
  price: null,
  type: null,
  tag: null
});
type Action = Visit['actions'][number];
const action = (
  order: number,
  verb: Action['verb'],
  extra: Partial<Action> = {}
): Action => ({
  kind: 'action',
  order,
  day: 1,
  hour: 1,
  frames: [order],
  command: '',
  verb,
  card: null,
  amount: null,
  target: null,
  result: null,
  outcome: 'done',
  error: null,
  gold: null,
  health: null,
  ...extra
});
const visit = (actions: Action[], extra: Partial<Visit> = {}): Visit => ({
  order: actions[0]?.order ?? 0,
  day: 1,
  hour: 1,
  kind: 'shop',
  encounter: null,
  actions,
  remaining: [],
  closed: true,
  battleId: null,
  gold: null,
  health: null,
  rerollFees: [],
  gap: false,
  ...extra
});
const moment = (responseSeq: number): Moment => ({
  responseSeq,
  snapshot: null,
  encounter: null,
  offer: null,
  chosen: null
});

test('the replay keeps every visit in order, merges consecutive moves on the last move, and charges each reroll', () => {
  const index: MomentIndex = {
    moments: new Map([1, 2, 3, 4, 5, 6, 7].map((n) => [n, moment(n)])),
    openings: new Map()
  };
  const [shop, empty] = replayVisits(
    [
      visit(
        [
          action(1, 'chose', { card: card('shop') }),
          action(2, 'rerolled'),
          action(3, 'rerolled'),
          action(4, 'moved', { card: card('a') }),
          action(5, 'moved', { card: card('b') }),
          action(6, 'bought', { outcome: 'no-response', card: card('c') }),
          action(7, 'left')
        ],
        { rerollFees: [1, 2] }
      ),
      visit([], { order: 9 })
    ],
    index
  );
  expect(
    shop.steps.map((s) => [s.seq, s.verb, s.count, s.amount, s.status])
  ).toEqual([
    [1, 'chose', 1, null, null],
    [2, 'rerolled', 1, -1, null],
    [3, 'rerolled', 1, -2, null],
    [5, 'moved', 2, null, null],
    [6, 'bought', 1, null, 'noResponse']
  ]);
  expect(shop.steps[3]).toMatchObject({
    card: null,
    moment: { responseSeq: 5 }
  });
  expect(empty.steps).toEqual([]);
});

test('a battle opening becomes a step at its response seq', () => {
  const opening = moment(3);
  const [fight] = replayVisits(
    [
      visit([action(1, 'chose'), action(5, 'moved')], {
        kind: 'pvp',
        battleId: 'b1'
      })
    ],
    { moments: new Map(), openings: new Map([['b1', opening]]) }
  );
  expect(fight.steps.map((s) => [s.seq, s.verb])).toEqual([
    [1, 'chose'],
    [3, 'pvp'],
    [5, 'moved']
  ]);
  expect(fight.steps[1].moment).toBe(opening);
});
