// @vitest-environment node

import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { beforeAll, describe, expect, test } from 'vite-plus/test';

import { buildDecisionPath } from '../src/features/runs/message-stream/decision-path';
import { tradeGold } from '../src/features/runs/message-stream/trade';
import {
  projectDecisions,
  type DecisionEntry
} from '../src/features/runs/message-stream/project';
import {
  SchemaIndex,
  TObject,
  translateMessage,
  countInputLeaves,
  countOutputLeaves,
  type Translated
} from '../src/features/runs/message-stream/translate';
import {
  readable,
  stringifyJson
} from '../src/features/runs/message-stream/readable';
import { MessageStream } from '../src/features/runs/message-stream/stream';
import { unwrapMessage } from '../src/features/runs/message-stream/wire';
import { decodeMessagePackValue } from '../src/features/runs/msgpack';
import {
  decodeRunPayload,
  type RunDetail
} from '../src/features/runs/run-payload';
import {
  FIXTURE_ACCOUNT_ID,
  FIXTURE_RUN_ID,
  fixtureStrings
} from '../tools/run-fixture/build';
import { SCHEMA } from './fixtures/game-messages';
import { encodeMessagePack } from './fixtures/run-payload';

const FIXTURES = resolve(import.meta.dirname, 'fixtures');
const PAYLOAD = resolve(FIXTURES, 'run-stream.payload.b64');
const READABLE_GOLDEN = resolve(FIXTURES, 'run-stream.readable.golden.json');
const PATH_GOLDEN = resolve(FIXTURES, 'run-stream.path.golden.txt');
const UPDATE = process.env.BPP_UPDATE_GOLDENS === '1';

function payload(): Uint8Array {
  return new Uint8Array(
    Buffer.from(readFileSync(PAYLOAD, 'utf8').trim(), 'base64')
  );
}

/** The golden's text; BPP_UPDATE_GOLDENS=1 (or a missing file) writes `actual` first. */
function golden(path: string, actual: string): string {
  if (UPDATE || !existsSync(path)) writeFileSync(path, actual);
  return readFileSync(path, 'utf8');
}

let detail: RunDetail;
let stream: MessageStream;

beforeAll(async () => {
  detail = await decodeRunPayload(payload(), FIXTURE_RUN_ID);
  stream = new MessageStream(detail.stream!, SCHEMA, detail.platform);
});

describe('Run Message Stream translation of a real Run (sanitized)', () => {
  test('every frame decodes, with no value the schema cannot name and none lost', () => {
    expect(detail.platform).toBe('desktop');
    expect(stream.frames).toHaveLength(727);
    const commands = stream.frames.filter((f) => f.message === null);
    expect(commands.every((f) => f.command !== null)).toBe(true);
    const messages = stream.frames.filter((f) => f.message !== null);
    const decoded = messages.map((f) => stream.message(f));
    expect(decoded.filter((d) => !d?.ok || d.flags.length > 0)).toEqual([]);
    // Every input scalar is in the translation.
    const lost = messages.filter((frame, i) => {
      const d = decoded[i];
      return (
        !d?.ok ||
        countOutputLeaves(d.value) !==
          countInputLeaves(
            decodeMessagePackValue(unwrapMessage(frame.message!).bytes)
          )
      );
    });
    expect(lost).toEqual([]);
    const blocks = new Set(decoded.map((d) => (d?.ok ? d.blocks : -1)));
    expect(messages).toHaveLength(378);
    // Plain, single-block and multi-block frames are all in the Run.
    expect([...blocks].sort((a, b) => a - b)).toEqual([0, 1, 2]);
  });

  test('a readable sample of each kind of frame matches its golden', () => {
    const kinds = new Map<string, number>();
    for (const frame of stream.frames) {
      const decoded = stream.message(frame);
      const kind =
        frame.command ??
        (decoded?.ok ? `${decoded.type}/${decoded.blocks}` : 'failed');
      if (!kinds.has(kind)) kinds.set(kind, frame.seq);
    }
    const sample = [...kinds.values()].sort((a, b) => a - b);
    const actual = `[\n${sample.map((seq) => stream.readableFrame(stream.frame(seq)!)).join(',\n')}\n]\n`;
    expect(actual).toBe(golden(READABLE_GOLDEN, actual));
  });

  test('a frame that fails to decode keeps its exact bytes and the error', () => {
    const frames = stream.frames.map((f) => ({ ...f }));
    const target = frames.find(
      (f) => f.message !== null && f.message.length > 1024
    )!;
    const corrupt = target.message!.slice();
    corrupt[corrupt.length - 3] ^= 0xff;
    target.message = corrupt;
    const broken = new MessageStream(frames, SCHEMA, 'desktop');
    const decoded = broken.message(broken.frame(target.seq)!);
    expect(decoded?.ok).toBe(false);
    const shown = JSON.parse(
      broken.readableFrame(broken.frame(target.seq)!)
    ) as {
      Seq: number;
      Message: { $bytes: string; $error: string };
    };
    expect(shown.Seq).toBe(target.seq);
    expect(new Uint8Array(Buffer.from(shown.Message.$bytes, 'base64'))).toEqual(
      corrupt
    );
    expect(shown.Message.$error).not.toBe('');
    // The other frames still translate.
    expect(
      broken.message(
        broken.frames.find((f) => f.message && f.seq !== target.seq)!
      )?.ok
    ).toBe(true);
  });

  test('every frame is reachable from the decision path, which matches its golden', async () => {
    const path = (await buildDecisionPath(detail))!;
    const reached = new Set(path.entries.flatMap((e) => e.frames));
    expect([...reached].sort((a, b) => a - b)).toEqual(
      stream.frames.map((f) => f.seq)
    );
    expect(path.entries.filter((e) => e.kind === 'battle')).toHaveLength(13);
    const actual =
      path.entries.map((e) => line(e, detail.cardNames)).join('\n') + '\n';
    expect(actual).toBe(golden(PATH_GOLDEN, actual));
  });
});

function card(
  c: {
    templateId: string | null;
    tier: string | null;
    price: number | null;
  } | null,
  names: Map<string, string>
): string {
  if (!c) return '';
  const name =
    (c.templateId && names.get(c.templateId)) ??
    c.templateId?.slice(0, 8) ??
    '?';
  return `${name}${c.tier ? `/${c.tier}` : ''}${c.price === null ? '' : `$${c.price}`}`;
}

function line(e: DecisionEntry, names: Map<string, string>): string {
  const at = `D${e.day ?? '-'}H${e.hour ?? '-'} ${e.frames.join(',')}`;
  switch (e.kind) {
    case 'offer':
      return `${at} offer ${e.state}${e.free ? ' free' : ''} ${card(e.encounter, names)} [${e.cards.map((c) => card(c, names)).join(', ')}]${e.rerollCost === null ? '' : ` reroll ${e.rerollCost}/${e.rerollsRemaining}`}`;
    case 'action':
      return `${at} ${e.verb} ${e.outcome} ${card(e.card, names)}${e.amount === null ? '' : ` amount ${e.amount}`}${e.result ? ` → ${e.result}` : ''}${e.gold === null || e.gold === 0 ? '' : ` gold ${e.gold}`}${e.health === null || e.health === 0 ? '' : ` health ${e.health}`}`;
    case 'battle':
      return `${at} battle ${e.battleId}`;
    default:
      return `${at} ${e.kind}`;
  }
}

describe('Fixture sanitization', () => {
  test('no removed id is left anywhere in the fixture', () => {
    const removed = new Set(
      JSON.parse(
        readFileSync(resolve(FIXTURES, 'run-stream.removed-ids.json'), 'utf8')
      ) as string[]
    );
    expect(removed.size).toBeGreaterThan(20);
    const left = [...fixtureStrings(payload())].filter((text) =>
      removed.has(createHash('sha256').update(text).digest('hex'))
    );
    expect(left).toEqual([]);
  });

  test('identities hold placeholders: Run, account, Battles, opponents', async () => {
    const root = decodeMessagePackValue(
      new Uint8Array((await import('node:zlib')).gunzipSync(payload()))
    ) as unknown[];
    expect(root[1]).toBe(FIXTURE_RUN_ID);
    expect(root[2]).toBe(FIXTURE_ACCOUNT_ID);
    for (const battle of root[5] as unknown[][]) {
      expect(battle[0]).toMatch(/^battle-\d\d$/);
      const [player, opponent] = battle[2] as unknown[][];
      expect(player.slice(0, 2)).toEqual([FIXTURE_ACCOUNT_ID, 'Player']);
      expect(opponent[0]).toMatch(/^00000000-0000-4000-8000-/);
      expect(opponent[1]).toMatch(/^Opponent \d+$/);
    }
    const bodies = (root[4] as unknown[][]).map(
      (event) => JSON.parse(event[3] as string) as Record<string, unknown>
    );
    expect(
      bodies.flatMap((b) => ('opponent_name' in b ? [b.opponent_name] : []))
    ).toEqual(
      expect.arrayContaining([expect.stringMatching(/^Opponent \d+$/)])
    );
    expect(
      bodies.flatMap((b) =>
        'opponent_name' in b && !/^Opponent \d+$/.test(String(b.opponent_name))
          ? [b.opponent_name]
          : 'battle_id' in b && !/^battle-\d\d$/.test(String(b.battle_id))
            ? [b.battle_id]
            : []
      )
    ).toEqual([]);
    const index = new SchemaIndex(SCHEMA);
    const runIds: Translated[] = [];
    const names: Translated[] = [];
    const titles: Translated[] = [];
    const ids: string[] = [];
    for (const frame of detail.stream!) {
      if (!frame.message) continue;
      const { value } = translateMessage(
        index,
        decodeMessagePackValue(unwrapMessage(frame.message).bytes)
      );
      walk(value, (o) => {
        if (o.type.endsWith('.NetMessageRunInitialized'))
          runIds.push(o.fields.RunId);
        if (!o.type.endsWith('.SimPvpOpponent')) return;
        names.push(o.fields.Name);
        titles.push(o.fields.TitlePrefix, o.fields.TitleSuffix);
        collect(o.fields.PlayerLoadout, ids);
        collect(o.fields.PlayerCollection, ids);
      });
    }
    expect(new Set(runIds)).toEqual(new Set([FIXTURE_RUN_ID]));
    const opponents = names.length;
    expect(
      names.filter((n) => typeof n !== 'string' || !/^Opponent \d+$/.test(n))
    ).toEqual([]);
    expect(
      titles.filter((t) => ![null, '', 'Title'].includes(t as never))
    ).toEqual([]);
    expect(
      ids.filter((id) => !id.startsWith('00000000-0000-4000-8000-'))
    ).toEqual([]);
    expect(opponents).toBeGreaterThan(0);
  });
});

function walk(value: Translated, visit: (o: TObject) => void) {
  if (Array.isArray(value)) value.forEach((v) => walk(v, visit));
  else if (value instanceof TObject) {
    visit(value);
    Object.values(value.fields).forEach((v) => walk(v, visit));
  }
}

function collect(value: Translated, out: string[]) {
  if (typeof value === 'string' && value !== '') out.push(value);
  else if (Array.isArray(value)) value.forEach((v) => collect(v, out));
  else if (value instanceof TObject)
    Object.values(value.fields).forEach((v) => collect(v, out));
}

describe('Translation rules', () => {
  const index = new SchemaIndex(SCHEMA);
  const translate = (message: unknown) =>
    translateMessage(index, decodeMessagePackValue(encodeMessagePack(message)));

  test('a value at an index no build names is kept raw and flagged', () => {
    const { value, flags } = translate([1, ['err', 0, 'text', 'new field']]);
    expect(stringifyJson(readable(value), 0).replace(/\n */g, '')).toContain(
      '"$extra": {"3": {"$raw": "new field","$flag": "unnamed index"}}'
    );
    expect(flags).toEqual([{ path: 'Message[3]', reason: 'unnamed index' }]);
    expect(countOutputLeaves(value)).toBe(5);
  });

  test('an unknown union case, enum value or shape is kept raw and flagged', () => {
    expect(translate([99, ['x']]).flags).toEqual([
      { path: 'Message', reason: 'unknown INetMessage case 99' }
    ]);
    const unknownEnum = translate([1, ['err', 12345, 'text']]);
    expect(readable(unknownEnum.value)).toMatchObject({
      ErrorType: '? (12345)'
    });
    expect(unknownEnum.flags).toEqual([
      { path: 'Message.ErrorType', reason: 'unknown EResponseErrorType value' }
    ]);
    const shape = translate([1, ['err', 'not a number', 'text']]);
    expect(readable(shape.value)).toMatchObject({
      ErrorType: { $raw: 'not a number', $flag: 'expected EResponseErrorType' }
    });
  });

  test('a key two builds name differently is shown with both names, not one', () => {
    const conflicted = structuredClone(SCHEMA);
    const error = Object.entries(conflicted.types).find(([n]) =>
      n.endsWith('.NetMessageError')
    )![1];
    if (error.kind !== 'object')
      throw new Error('NetMessageError is not an object');
    error.members.push({
      key: 2,
      name: 'Text',
      type: 'System.String',
      sources: ['macos-99999']
    });
    const { value, flags } = translateMessage(
      new SchemaIndex(conflicted),
      decodeMessagePackValue(encodeMessagePack([1, ['err', 0, 'text']]))
    );
    expect(readable(value)).toMatchObject({
      $extra: {
        '2': {
          $raw: 'text',
          $flag: 'index 2 is Message: String or Text: String across builds'
        }
      }
    });
    expect(flags).toHaveLength(1);
  });

  test('a 64-bit value prints exactly in readable JSON', () => {
    const value = translate([1, ['err', 0, 'text']]).value;
    expect(
      stringifyJson({ big: 9007199254740993n, value: readable(value) }, 0)
    ).toContain('9007199254740993');
  });

  test('a member at another index in another build is kept raw at both indices', () => {
    const moved = structuredClone(SCHEMA);
    const error = Object.entries(moved.types).find(([n]) =>
      n.endsWith('.NetMessageError')
    )![1];
    if (error.kind !== 'object')
      throw new Error('NetMessageError is not an object');
    error.members.push({
      key: 1,
      name: 'MessageId',
      type: 'System.String',
      sources: ['macos-99999']
    });
    const { value, flags } = translateMessage(
      new SchemaIndex(moved),
      decodeMessagePackValue(encodeMessagePack([1, ['7', null, 'text']]))
    );
    expect(readable(value)).toMatchObject({
      $extra: {
        '0': { $raw: '7', $flag: 'MessageId is at index 0 or 1 across builds' },
        '1': { $raw: null, $flag: 'MessageId is at index 0 or 1 across builds' }
      }
    });
    expect(readable(value)).not.toHaveProperty('MessageId');
    expect(flags).toHaveLength(2);
  });

  test('readable JSON keeps each union tag with the qualified type', () => {
    const { value } = translate([1, ['err', 0, 'text']]);
    expect(readable(value)).toMatchObject({
      $type: 'BazaarGameShared.Infra.Messages.NetMessageError',
      $tag: 1
    });
  });
});

describe('Frames as a reader sees them', () => {
  test('CommandArgsJson stays the recorded text; a parse that would round it is left out', () => {
    const frames = [
      {
        seq: 1,
        timestampMs: 0,
        message: null,
        respondsToSeq: null,
        command: 'SendSelectItem',
        commandArgsJson: '{"instanceId":9007199254740993}'
      },
      {
        seq: 2,
        timestampMs: 0,
        message: null,
        respondsToSeq: null,
        command: 'SendSellCard',
        commandArgsJson: '{"cardId":"itm_a"}'
      }
    ];
    const s = new MessageStream(frames, SCHEMA, 'desktop');
    const big = s.readableFrame(frames[0]);
    expect(big).toContain(
      '"CommandArgsJson": "{\\"instanceId\\":9007199254740993}"'
    );
    expect(big).not.toContain('"CommandArgs":');
    expect(JSON.parse(s.readableFrame(frames[1]))).toMatchObject({
      CommandArgsJson: '{"cardId":"itm_a"}',
      CommandArgs: { cardId: 'itm_a' }
    });
  });

  test('a projection keeps few decoded trees, and the export is built in batches', async () => {
    const fresh = new MessageStream(detail.stream!, SCHEMA, detail.platform);
    await projectDecisions(fresh, [], 0);
    expect(fresh.cachedMessages).toBeLessThanOrEqual(16);
    const exported = fresh.exportFrames();
    expect(exported).toBeInstanceOf(Promise);
    const parsed = JSON.parse((await exported).join('')) as unknown[];
    expect(parsed).toHaveLength(727);
    expect(fresh.cachedMessages).toBeLessThanOrEqual(16);
  });

  test('the real Run shows recorded prices next to a differing balance change', async () => {
    const path = (await buildDecisionPath(detail))!;
    const at = (seqs: number[]) =>
      path.entries.find(
        (e) => e.kind === 'action' && e.frames.join() === seqs.join()
      ) as Extract<DecisionEntry, { kind: 'action' }>;
    expect([at([559, 560]).verb, tradeGold(at([559, 560]))]).toEqual([
      'bought',
      { amount: 16, net: -13 }
    ]);
    expect([at([167, 168]).verb, tradeGold(at([167, 168]))]).toEqual([
      'sold',
      { amount: 1, net: 6 }
    ]);
  });
});
