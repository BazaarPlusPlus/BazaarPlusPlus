import { expect, test } from 'vite-plus/test';
import { projectDecisions } from '../message-stream/project';
import { MessageStream } from '../message-stream/stream';
import {
  command,
  enumValue,
  eventTag,
  gameSim,
  message,
  SCHEMA
} from './fixtures/game-messages';
import { encodeMessagePack } from './fixtures/encode';

const template = 'aaaaaaaa-0000-0000-0000-000000000001';
const replacement = 'bbbbbbbb-0000-0000-0000-000000000002';
const player = enumValue('ECombatantId', 'Player'),
  hand = enumValue('EInventorySection', 'Hand'),
  stash = enumValue('EInventorySection', 'Stash'),
  item = enumValue('ECardType', 'Item');
const original = [
  'a',
  template,
  new Map(),
  enumValue('EEnchantmentType', 'Fiery'),
  [],
  [],
  [],
  enumValue('ETier', 'Gold'),
  item,
  enumValue('ECardSize', 'Small'),
  player,
  1,
  hand
];
function baseline(cards: unknown) {
  return encodeMessagePack([
    4,
    [
      [
        null,
        null,
        [
          enumValue('EHero', 'Vanessa'),
          new Map([[enumValue('EPlayerAttributeType', 'Gold'), 7]])
        ],
        cards
      ],
      'sync'
    ]
  ]);
}
async function replay(responses: Uint8Array[]) {
  const frames = responses.flatMap((bytes, i) => [
    command(2 * i + 1, 'SendMoveItem'),
    message(2 * i + 2, bytes, 2 * i + 1)
  ]);
  const path = await projectDecisions(
    new MessageStream(frames, SCHEMA, 'desktop'),
    [],
    0
  );
  return [...path.moments.values()].map((m) => m.snapshot);
}
const event = (name: string, args: unknown[]) =>
  gameSim({ events: [[eventTag(name), args]] });

test('a truncated placement cannot establish inventory, while observed totals and a later complete sync survive', async () => {
  const [partial, unreadable, empty, complete] = await replay([
    baseline([original.slice(0, 10)]),
    baseline(42),
    baseline([]),
    baseline([original])
  ]);
  expect(partial).toMatchObject({
    board: null,
    stash: null,
    skills: null,
    gold: 7
  });
  expect(unreadable).toMatchObject({
    board: null,
    stash: null,
    skills: null,
    gold: 7
  });
  expect(empty).toMatchObject({ board: [], stash: [], skills: [] });
  expect(complete?.board).toHaveLength(1);
});

test('move to stash socket zero, same-instance transform, reversion and disposal retain only observed facts', async () => {
  const record = ['a', template, item, player, stash, 0];
  const [start, moved, transformed, reverted, normal, disposed, sparse] =
    await replay([
      baseline([original]),
      event('GameSimEventCardMoved', ['a', player, stash, 0]),
      event('GameSimEventCardTransformed', [
        'exec',
        'a',
        [['a', replacement, item, player, stash, 0]]
      ]),
      event('GameSimEventCardTransformReverted', [record, ['a']]),
      event('GameSimEventCardEnchanted', ['a', null, true]),
      event('GameSimEventCardDisposed', ['a', 0]),
      gameSim({ prices: { a: 4 } })
    ]);
  expect(start?.board?.[0]).toMatchObject({ slot: 1, tier: 'Gold' });
  expect(moved?.board).toEqual([]);
  expect(moved?.stash?.[0]).toMatchObject({ slot: 0, enchant: 'Fiery' });
  expect(transformed?.stash).toBeNull();
  expect(reverted?.stash?.[0]).toMatchObject({
    slot: 0,
    size: 1,
    templateId: template,
    tier: 'Gold',
    enchant: 'Fiery'
  });
  expect(normal?.stash?.[0].enchant).toBeNull();
  expect(disposed?.stash).toEqual([]);
  expect(sparse?.stash).toBe(disposed?.stash);
});

test('skills follow observed acquisition order and exclude an opponent equip', async () => {
  const skill = (id: string) =>
    [
      ...original.slice(0, 8),
      enumValue('ECardType', 'Skill'),
      original[9],
      null,
      null,
      null
    ].map((value, i) => (i === 0 ? id : value));
  const [, first, second, opponent] = await replay([
    baseline([skill('a'), skill('b'), skill('c')]),
    event('GameSimEventPlayerSkillEquipped', ['b', player]),
    event('GameSimEventPlayerSkillEquipped', ['a', player]),
    event('GameSimEventPlayerSkillEquipped', [
      'c',
      enumValue('ECombatantId', 'Opponent')
    ])
  ]);
  expect(first?.skills?.map((c) => c.instanceId)).toEqual(['b']);
  expect(second?.skills?.map((c) => c.instanceId)).toEqual(['b', 'a']);
  expect(opponent?.skills).toBe(second?.skills);
});
