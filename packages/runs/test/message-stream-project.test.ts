import { describe, expect, test } from 'vite-plus/test';

import {
  projectDecisions,
  type DecisionEntry
} from '../message-stream/project';
import { MessageStream, type StreamFrame } from '../message-stream/stream';
import {
  SCHEMA,
  command,
  events,
  gameSim,
  message,
  netError,
  stateSync
} from './fixtures/game-messages';
import { projectVisits } from '../message-stream/visits';
import { encodeMessagePack } from './fixtures/encode';

const SHOP = { state: 'Encounter', selection: ['itm_a', 'itm_b'] };

function opening(): StreamFrame[] {
  return [
    command(1, 'SendSelectEncounter', { id: 'enc_1' }),
    message(
      2,
      gameSim({
        day: 1,
        hour: 1,
        gold: 10,
        health: 300,
        events: [
          events.purchased('enc_1', 0),
          events.dealt('itm_a', 'aaaaaaaa-0000-0000-0000-000000000001'),
          events.dealt('itm_b', 'bbbbbbbb-0000-0000-0000-000000000002')
        ],
        prices: { itm_a: 4, itm_b: 6 },
        current: SHOP
      }),
      1
    )
  ];
}

async function project(frames: StreamFrame[]): Promise<DecisionEntry[]> {
  return (
    await projectDecisions(new MessageStream(frames, SCHEMA, 'desktop'), [], 0)
  ).entries;
}

function only<K extends DecisionEntry['kind']>(
  entries: DecisionEntry[],
  kind: K
): Array<Extract<DecisionEntry, { kind: K }>> {
  return entries.filter(
    (e): e is Extract<DecisionEntry, { kind: K }> => e.kind === kind
  );
}

describe('Decision Path projection', () => {
  test('tag observations follow the instance across sparse updates and freeze on the selected parent', async () => {
    const frames = [
      message(
        1,
        stateSync({
          day: 1,
          hour: 0,
          gold: 10,
          health: 300,
          state: 'Choice',
          selection: ['second'],
          cards: [
            {
              id: 'first',
              templateId: 'same',
              type: 'EventEncounter',
              price: 0,
              tags: ['Merchant']
            },
            {
              id: 'second',
              templateId: 'same',
              type: 'EventEncounter',
              price: 0,
              tags: ['Event']
            }
          ]
        })
      ),
      message(2, gameSim({ prices: { second: 5 } })),
      command(3, 'SendSelectEncounter', { id: 'second' }),
      message(
        4,
        gameSim({
          day: 1,
          hour: 1,
          events: [events.purchased('second', 0)],
          current: {
            state: 'Encounter',
            encounter: 'same',
            selection: ['reward']
          }
        }),
        3
      ),
      message(5, gameSim({ tags: { second: [] } })),
      command(6, 'SendMoveItem', { id: 'second' }),
      message(7, gameSim({ tags: { second: ['Merchant'] } })),
      command(8, 'SendMoveItem', { id: 'second' }),
      message(9, gameSim({ events: [events.dealt('second', 'replacement')] })),
      command(10, 'SendMoveItem', { id: 'second' })
    ];
    const { entries, contexts } = await projectDecisions(
      new MessageStream(frames, SCHEMA, 'desktop'),
      [],
      0
    );
    expect(only(entries, 'action').map((a) => a.card?.tag)).toEqual([
      'Event',
      null,
      'Merchant',
      null
    ]);
    expect(only(entries, 'offer')[1].encounter).toMatchObject({
      instanceId: 'second',
      tag: 'Event'
    });
    expect(projectVisits(entries, contexts)[0]).toMatchObject({
      kind: 'event',
      hour: 1
    });
  });

  test('an offer keeps each card and price as it was shown', async () => {
    const entries = await project([
      ...opening(),
      // The price changes after the offer was shown.
      message(3, gameSim({ prices: { itm_a: 9 } }))
    ]);
    const [offer] = only(entries, 'offer');
    expect(
      offer.cards.map((c) => [c.instanceId, c.templateId, c.price])
    ).toEqual([
      ['itm_a', 'aaaaaaaa-0000-0000-0000-000000000001', 4],
      ['itm_b', 'bbbbbbbb-0000-0000-0000-000000000002', 6]
    ]);
    expect(offer.day).toBe(1);
    expect(offer.hour).toBe(1);
  });

  test('offers and actions keep the enchant observed at their frame', async () => {
    const id = 'itm_a';
    const sync = (enchant?: string) =>
      stateSync({
        day: 1,
        hour: 1,
        gold: 10,
        health: 300,
        state: 'Encounter',
        selection: [id],
        cards: [{ id, templateId: 'aaaa', price: 4, enchant }]
      });
    const select = (seq: number) =>
      command(seq, 'SendSelectItem', { instanceId: id });
    const entries = await project([
      message(1, sync('Deadly')),
      command(2, 'SendCommitToPedestal', { cardId: 'itm_a' }),
      message(3, gameSim({ events: [events.enchanted('itm_a', 'Shiny')] }), 2),
      message(4, gameSim({ prices: { itm_a: 5 } })),
      select(5),
      message(6, gameSim({ enchants: { itm_a: 'Icy' } })),
      select(7),
      message(8, gameSim({ events: [events.dealt('itm_a', 'bbbb')] })),
      select(9),
      message(10, gameSim({ enchants: { itm_a: 'Turbo' } })),
      message(11, sync()),
      select(12)
    ]);
    expect(only(entries, 'offer')[0].cards[0].enchant).toBe('Deadly');
    const actions = only(entries, 'action');
    expect(actions[0]).toMatchObject({ verb: 'enchanted', result: 'Shiny' });
    expect(actions.map((action) => action.card?.enchant)).toEqual([
      'Deadly',
      'Shiny',
      'Icy',
      null,
      null
    ]);
  });

  test('an AttributeDelta Delete removes the price', async () => {
    const entries = await project([
      ...opening(),
      message(3, gameSim({ prices: { itm_a: null } })),
      command(4, 'SendReRollSelection'),
      message(
        5,
        gameSim({
          events: [
            events.dealt('itm_c', 'cccccccc-0000-0000-0000-000000000003')
          ],
          current: { state: 'Encounter', selection: ['itm_a', 'itm_c'] }
        }),
        4
      )
    ]);
    const offers = only(entries, 'offer');
    expect(offers[1].cards.map((c) => [c.instanceId, c.price])).toEqual([
      ['itm_a', null],
      ['itm_c', null]
    ]);
  });

  test('a command is done only with positive evidence from its own responses', async () => {
    const entries = await project([
      ...opening(),
      command(3, 'SendSelectItem', { instanceId: 'itm_a' }),
      message(
        4,
        gameSim({ gold: 6, events: [events.purchased('itm_a', 4)] }),
        3
      ),
      command(5, 'SendSelectItem', { instanceId: 'itm_b' }),
      message(6, netError('Not enough gold'), 5),
      command(7, 'SendSelectItem', { instanceId: 'itm_b' }),
      // An unrelated purchase does not confirm this command.
      command(8, 'SendSelectItem', { instanceId: 'itm_b' }),
      message(9, gameSim({ events: [events.purchased('itm_x', 1)] }), 8)
    ]);
    const actions = only(entries, 'action').slice(1);
    expect(
      actions.map((a) => [a.verb, a.outcome, a.amount, a.gold, a.error])
    ).toEqual([
      ['bought', 'done', 4, -4, null],
      [
        'bought',
        'attempted',
        null,
        0,
        expect.stringContaining('Not enough gold')
      ],
      // Nothing answered, so no change was observed either.
      ['bought', 'no-response', null, null, null],
      ['bought', 'attempted', null, 0, null]
    ]);
    expect(actions[0].card?.templateId).toBe(
      'aaaaaaaa-0000-0000-0000-000000000001'
    );
  });

  test('a pick is free by the game’s SelectionIsFree, not by its price', async () => {
    const entries = await project([
      command(1, 'SendSelectEncounter', { id: 'enc_1' }),
      message(
        2,
        gameSim({
          day: 1,
          gold: 10,
          events: [events.purchased('enc_1', 0), events.dealt('itm_a', 'aaaa')],
          prices: { itm_a: 4 },
          current: { state: 'Encounter', selection: ['itm_a'], free: true }
        }),
        1
      ),
      command(3, 'SendSelectItem', { instanceId: 'itm_a' }),
      message(4, gameSim({ events: [events.purchased('itm_a', 0)] }), 3)
    ]);
    const [offer] = only(entries, 'offer');
    expect(offer.free).toBe(true);
    expect(only(entries, 'action').at(-1)).toMatchObject({
      verb: 'picked',
      outcome: 'done'
    });
  });

  test('a reconnect replaces the whole state as a new baseline', async () => {
    const entries = await project([
      ...opening(),
      command(3, 'ReestablishSession'),
      message(
        4,
        stateSync({
          day: 3,
          hour: 2,
          gold: 20,
          health: 500,
          state: 'Encounter',
          selection: ['itm_z'],
          cards: [{ id: 'itm_z', templateId: 'ZZZZ', price: 7 }]
        }),
        3
      ),
      command(5, 'SendSelectItem', { instanceId: 'itm_z' }),
      message(
        6,
        gameSim({ gold: 13, events: [events.purchased('itm_z', 7)] }),
        5
      )
    ]);
    const offers = only(entries, 'offer');
    expect(offers.at(-1)).toMatchObject({ day: 3, hour: 2 });
    expect(offers.at(-1)?.cards).toEqual([
      {
        instanceId: 'itm_z',
        tag: null,
        templateId: 'zzzz',
        size: 1,
        type: 'Item',
        tier: 'Bronze',
        enchant: null,
        price: 7
      }
    ]);
    const actions = only(entries, 'action');
    expect(actions.find((a) => a.verb === 'reconnect')?.outcome).toBe('done');
    // Gold is measured against the new baseline, not the state before the reconnect.
    expect(actions.at(-1)).toMatchObject({
      outcome: 'done',
      gold: -7,
      card: { size: 1 }
    });
  });

  test('an undecodable message suspends inferences until a baseline returns', async () => {
    const entries = await project([
      ...opening(),
      command(3, 'SendReRollSelection'),
      message(4, Uint8Array.of(0x92, 0x03), 3),
      command(5, 'SendSelectItem', { instanceId: 'itm_a' }),
      message(
        6,
        gameSim({
          gold: 2,
          events: [events.purchased('itm_a', 4), events.dealt('itm_q', 'qqqq')],
          current: { state: 'Encounter', selection: ['itm_q'] }
        }),
        5
      ),
      message(
        7,
        stateSync({
          day: 2,
          hour: 0,
          gold: 2,
          health: 300,
          state: 'Choice',
          selection: ['enc_9'],
          cards: []
        })
      )
    ]);
    const undecoded = only(entries, 'undecoded');
    expect(undecoded.map((e) => e.frames)).toEqual([[4]]);
    const buy = only(entries, 'action').find(
      (a) => a.command === 'SendSelectItem'
    );
    // The purchase event is still evidence; the gold change is not trusted.
    expect(buy).toMatchObject({ outcome: 'done', gold: null });
    // No offer is read from the suspended state; the baseline shows one again.
    expect(only(entries, 'offer').map((o) => o.frames)).toEqual([[2], [7]]);
    expect(only(entries, 'reconnect').map((o) => o.frames)).toEqual([[7]]);
  });

  test('a Battle follows the combat it was fought in: PvE after Combat, PvP after PVPCombat', async () => {
    const frames = [
      command(1, 'SendSelectEncounter', { id: 'com_1' }),
      message(
        2,
        gameSim({
          day: 2,
          hour: 3,
          events: [events.purchased('com_1', 0)],
          current: { state: 'Combat', selection: [] }
        }),
        1
      ),
      command(4, 'SendSelectEncounter', { id: 'pvp_1' }),
      message(
        5,
        gameSim({
          day: 2,
          hour: 6,
          events: [events.purchased('pvp_1', 0)],
          current: { state: 'PVPCombat', selection: [] }
        }),
        4
      ),
      message(
        7,
        gameSim({
          day: 3,
          hour: 0,
          events: [events.dealt('enc_9', 'eeee')],
          current: { state: 'Choice', selection: ['enc_9'] }
        }),
        4
      )
    ];
    const battles = [
      { id: 'pve', seq: 3, recordedAtMs: 0, day: 2, hour: 3, pvp: false },
      { id: 'pvp', seq: 8, recordedAtMs: 0, day: 2, hour: 6, pvp: true }
    ];
    const { entries } = await projectDecisions(
      new MessageStream(frames, SCHEMA, 'desktop'),
      battles,
      0
    );
    expect(
      entries.map((e) =>
        e.kind === 'battle'
          ? `battle ${e.battleId}`
          : e.kind === 'action'
            ? `${e.verb} ${e.frames.join(',')}`
            : e.kind
      )
    ).toEqual([
      'chose 1,2',
      'battle pve',
      'chose 4,5,7',
      'battle pvp',
      'offer'
    ]);
  });

  test('a state message the schema could not fully read suspends inferences too', async () => {
    const broken = encodeMessagePack([
      3,
      [[[], [0, new Map()], [1, new Map()], new Map(), null, 42, []], 'm']
    ]);
    const entries = await project([
      ...opening(),
      message(3, broken),
      message(
        4,
        gameSim({
          events: [events.dealt('itm_q', 'qqqq')],
          current: { state: 'Encounter', selection: ['itm_q'] }
        })
      )
    ]);
    expect(only(entries, 'undecoded')).toMatchObject([
      { frames: [3], error: expect.stringContaining('CurrentState') }
    ]);
    expect(only(entries, 'offer').map((o) => o.frames)).toEqual([[2]]);
  });

  test('a reconnect is a baseline: no gold is credited across it, and its offer is shown afresh', async () => {
    const entries = await project([
      ...opening(),
      command(3, 'ReestablishSession'),
      message(
        4,
        stateSync({
          day: 1,
          hour: 1,
          gold: 20,
          health: 300,
          state: 'Encounter',
          // The same card, now at another price.
          selection: ['itm_a'],
          cards: [{ id: 'itm_a', templateId: 'aaaa', price: 7 }]
        }),
        3
      ),
      message(5, gameSim({ gold: 20 }), 3)
    ]);
    const reconnect = only(entries, 'action').find(
      (a) => a.verb === 'reconnect'
    );
    expect(reconnect).toMatchObject({ outcome: 'done', gold: 0 });
    const offers = only(entries, 'offer');
    expect(offers.map((o) => [o.frames, o.cards.map((c) => c.price)])).toEqual([
      [[2], [4, 6]],
      [[4], [7]]
    ]);
  });

  test('gold is summed over the paired responses only, not over other messages between', async () => {
    const entries = await project([
      ...opening(),
      command(3, 'SendSelectItem', { instanceId: 'itm_a' }),
      // A message the server sent on its own, before the answer.
      message(4, gameSim({ gold: 30 })),
      message(
        5,
        gameSim({ gold: 26, events: [events.purchased('itm_a', 4)] }),
        3
      )
    ]);
    expect(only(entries, 'action').at(-1)).toMatchObject({
      outcome: 'done',
      amount: 4,
      gold: -4
    });
  });

  test('a pedestal is done only when its own card changes; a reroll only when new cards are offered', async () => {
    const entries = await project([
      ...opening(),
      command(3, 'SendCommitToPedestal', { cardId: 'itm_a' }),
      message(4, gameSim({ events: [events.upgraded('itm_b', 'Silver')] }), 3),
      command(5, 'SendCommitToPedestal', { cardId: 'itm_b' }),
      message(6, gameSim({ events: [events.upgraded('itm_b', 'Gold')] }), 5),
      command(7, 'SendReRollSelection'),
      // A card spawned elsewhere; the offer did not change.
      message(
        8,
        gameSim({ events: [events.dealt('itm_x', 'xxxx')], current: SHOP }),
        7
      )
    ]);
    expect(
      only(entries, 'action')
        .slice(1)
        .map((a) => [a.verb, a.outcome, a.result])
    ).toEqual([
      ['committed', 'attempted', null],
      ['upgraded', 'done', 'Gold'],
      ['rerolled', 'attempted', null]
    ]);
  });
});
