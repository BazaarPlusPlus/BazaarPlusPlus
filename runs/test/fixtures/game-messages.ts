import schemaJson from '../../message-stream/game-message-schema.json';
import type { GameMessageSchema } from '../../message-stream/schema';
import type { StreamFrame } from '../../message-stream/stream';
import { encodeMessagePack } from './encode';

export const SCHEMA = schemaJson as GameMessageSchema;

/** An enum member's value in the committed schema, so tests name members, not numbers. */
export function enumValue(type: string, name: string): number {
  const found = Object.entries(SCHEMA.types).find(([n]) =>
    n.endsWith(`.${type}`)
  )?.[1];
  const value =
    found?.kind === 'enum'
      ? found.values.find((v) => v.name === name)?.value
      : undefined;
  if (value === undefined) throw new Error(`No ${type}.${name}`);
  return Number(value);
}

const attr = (name: string) => enumValue('EPlayerAttributeType', name);
const cardAttr = (name: string) => enumValue('ECardAttributeType', name);
const state = (name: string) => enumValue('ERunState', name);

/** IGameSimEvent cases, by their union tag in the schema. */
export function eventTag(type: string): number {
  const union = Object.entries(SCHEMA.types).find(([n]) =>
    n.endsWith('.IGameSimEvent')
  )?.[1];
  const tag =
    union?.kind === 'union'
      ? union.cases.find((c) => c.type.endsWith(`.${type}`))?.tag
      : undefined;
  if (tag === undefined) throw new Error(`No event ${type}`);
  return tag;
}

export const events = {
  dealt: (id: string, templateId: string) => [
    eventTag('GameSimEventCardDealt'),
    [id, templateId, enumValue('ECardType', 'Item')]
  ],
  enchanted: (id: string, enchant: string) => [
    eventTag('GameSimEventCardEnchanted'),
    [id, enumValue('EEnchantmentType', enchant), false]
  ],
  purchased: (id: string, price: number) => [
    eventTag('GameSimEventCardPurchased'),
    [id, price, null, null, null]
  ],
  upgraded: (id: string, tier: string) => [
    eventTag('GameSimEventCardUpgraded'),
    [id, enumValue('ETier', tier)]
  ]
};

export type SimSpec = {
  events?: unknown[];
  gold?: number;
  health?: number;
  day?: number;
  hour?: number;
  sizes?: Record<string, string>;
  tags?: Record<string, string[]>;
  enchants?: Record<string, string>;
  /** Card id → BuyPrice; null deletes the attribute (AttributeDelta Delete). */
  prices?: Record<string, number | null>;
  current?: {
    state: string;
    selection: string[];
    free?: boolean;
    encounter?: string;
  };
};

function rules(free: boolean) {
  return [false, free, false, null, false, null];
}

/** A NetMessageGameSim, `[3, [GameSim, MessageId]]`, as the game writes it. */
export function gameSim(spec: SimSpec): Uint8Array {
  const player = new Map<number, number>();
  if (spec.gold !== undefined) player.set(attr('Gold'), spec.gold);
  if (spec.health !== undefined) player.set(attr('Health'), spec.health);
  const cards = new Map<string, unknown>();
  for (const [id, price] of Object.entries(spec.prices ?? {})) {
    const delta =
      price === null
        ? [enumValue('EAttributeDeltaType', 'Delete'), 0]
        : [enumValue('EAttributeDeltaType', 'Update'), price];
    cards.set(id, [
      id,
      new Map([[cardAttr('BuyPrice'), delta]]),
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      0
    ]);
  }
  for (const [id, size] of Object.entries(spec.sizes ?? {})) {
    cards.set(id, [
      id,
      null,
      null,
      null,
      null,
      null,
      enumValue('ECardSize', size)
    ]);
  }
  for (const [id, enchant] of Object.entries(spec.enchants ?? {})) {
    cards.set(id, [id, new Map(), enumValue('EEnchantmentType', enchant)]);
  }
  for (const [id, tags] of Object.entries(spec.tags ?? {})) {
    cards.set(id, [
      id,
      new Map(),
      null,
      null,
      null,
      null,
      null,
      tags.map((name) => enumValue('ECardTag', name))
    ]);
  }
  return encodeMessagePack([
    3,
    [
      [
        spec.events ?? [],
        [0, player],
        [1, new Map()],
        cards,
        spec.day === undefined
          ? null
          : [spec.day, spec.hour ?? 0, 0, 0, false, 0, '5.0.0'],
        spec.current
          ? [
              state(spec.current.state),
              spec.current.encounter ?? null,
              null,
              null,
              spec.current.selection,
              null,
              rules(spec.current.free ?? false)
            ]
          : null,
        []
      ],
      'msg'
    ]
  ]);
}

/** A NetMessageGameStateSync: the whole state after a reconnect. */
export function stateSync(spec: {
  day: number;
  hour: number;
  gold: number;
  health: number;
  state: string;
  selection: string[];
  cards: Array<{
    id: string;
    templateId: string;
    price: number;
    enchant?: string;
    tags?: string[];
    type?: string;
  }>;
}): Uint8Array {
  return encodeMessagePack([
    4,
    [
      [
        [
          '818c1c4b-4ddf-4d06-b369-e2a56c1cfb2d',
          spec.day,
          spec.hour,
          0,
          0,
          false,
          '5.0.0'
        ],
        [
          state(spec.state),
          null,
          null,
          null,
          null,
          spec.selection,
          rules(false)
        ],
        [
          enumValue('EHero', 'Vanessa'),
          new Map([
            [attr('Gold'), spec.gold],
            [attr('Health'), spec.health]
          ]),
          0
        ],
        spec.cards.map((c) => [
          c.id,
          c.templateId,
          new Map([[cardAttr('BuyPrice'), c.price]]),
          c.enchant ? enumValue('EEnchantmentType', c.enchant) : null,
          [],
          [],
          (c.tags ?? []).map((name) => enumValue('ECardTag', name)),
          enumValue('ETier', 'Bronze'),
          enumValue('ECardType', c.type ?? 'Item'),
          enumValue('ECardSize', 'Small'),
          null,
          null,
          null
        ])
      ],
      'syn'
    ]
  ]);
}

/** A NetMessageError. */
export function netError(text: string): Uint8Array {
  return encodeMessagePack([
    1,
    ['err', enumValue('EResponseErrorType', firstErrorType()), text]
  ]);
}

function firstErrorType(): string {
  const type = Object.entries(SCHEMA.types).find(([n]) =>
    n.endsWith('.EResponseErrorType')
  )?.[1];
  if (type?.kind !== 'enum') throw new Error('No EResponseErrorType');
  return type.values[0].name;
}

let clock = 1_800_000_000_000;

export function command(
  seq: number,
  name: string,
  args: Record<string, unknown> = {}
): StreamFrame {
  return {
    seq,
    timestampMs: clock++,
    message: null,
    respondsToSeq: null,
    command: name,
    commandArgsJson: JSON.stringify(args)
  };
}

export function message(
  seq: number,
  bytes: Uint8Array,
  respondsToSeq: number | null = null
): StreamFrame {
  return {
    seq,
    timestampMs: clock++,
    message: bytes,
    respondsToSeq,
    command: null,
    commandArgsJson: null
  };
}
