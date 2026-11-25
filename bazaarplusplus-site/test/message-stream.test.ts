// @vitest-environment node

import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { beforeAll, describe, expect, test, vi } from 'vite-plus/test';

import { buildDecisionPath } from '../../runs/message-stream/decision-path';
import { projectVisits } from '../../runs/message-stream/visits';
import {
  projectDecisions,
  type DecisionEntry
} from '../../runs/message-stream/project';
import {
  SchemaIndex,
  TObject,
  translateMessage,
  countInputLeaves,
  countOutputLeaves,
  type Translated
} from '../../runs/message-stream/translate';
import { MessageStream } from '../../runs/message-stream/stream';
import { unwrapMessage } from '../../runs/message-stream/wire';
import { decodeMessagePackValue } from '../../runs/msgpack';
import {
  decodeRunPayload,
  type RunDetail
} from '../src/features/runs/run-payload';
import {
  FIXTURE_ACCOUNT_ID,
  FIXTURE_RUN_ID,
  fixtureStrings
} from '../tools/run-fixture/build';
import { SCHEMA } from '../../runs/test/fixtures/game-messages';

// Full 727-frame replays yield between batches. Background test workers can take over five seconds.
vi.setConfig({ testTimeout: 60_000 });

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

  test('each fight opening exactly matches the captured player and retains the selected offer through a fuse', async () => {
    const path = (await buildDecisionPath(detail))!;
    const anchors = [
      66, 125, 186, 233, 305, 360, 415, 498, 541, 572, 615, 702, 739
    ];
    expect(detail.battles).toHaveLength(anchors.length);
    for (const [index, battle] of detail.battles.entries()) {
      const opening = path.openings.get(battle.id);
      expect({ [battle.id]: opening?.responseSeq }).toEqual({
        [battle.id]: anchors[index]
      });
      for (const section of ['hand', 'skills'] as const) {
        const expected = battle[section];
        expect(expected.status).not.toBe('missing');
        const actual =
          section === 'hand'
            ? opening?.snapshot?.board
            : opening?.snapshot?.skills;
        const fields = (
          cards: readonly {
            instanceId: string;
            templateId: string | null;
            tier: string | null;
            enchant: string | null;
            slot?: number | null;
            size?: number | null;
          }[]
        ) =>
          cards
            .map((c) => ({
              instanceId: c.instanceId,
              templateId: c.templateId,
              tier: c.tier,
              enchant: c.enchant,
              ...(section === 'hand' ? { slot: c.slot, size: c.size } : {})
            }))
            .sort((a, b) => a.instanceId.localeCompare(b.instanceId));
        const location = [
          battle.id,
          battle.day,
          battle.hour,
          section,
          anchors[index]
        ].join('/');
        expect({ [location]: actual && fields(actual) }).toEqual({
          [location]: fields(expected.cards)
        });
      }
    }
    const before = path.moments.get(365),
      after = path.moments.get(367);
    expect(after?.responseSeq).toBe(368);
    expect(after?.chosen).toBe('skl_sLZZBEV');
    expect(after?.offer?.map((c) => c.instanceId)).toContain(after?.chosen);
    expect(
      before?.snapshot?.skills
        ?.filter((c) => c.instanceId === 'skl_sUEjzLM')
        .map((c) => c.tier)
    ).toEqual(['Silver']);
    expect(
      after?.snapshot?.skills
        ?.filter((c) => c.instanceId === 'skl_sUEjzLM')
        .map((c) => c.tier)
    ).toEqual(['Gold']);
    expect(after?.snapshot?.skills?.map((c) => c.instanceId)).not.toContain(
      after?.chosen
    );
    expect(path.moments.get(263)?.snapshot).toBeNull();
    expect(path.moments.get(6)?.encounter?.instanceId).toBe(
      path.moments.get(6)?.chosen
    );
    const pve = await projectDecisions(
      stream,
      [
        {
          id: 'first-monster',
          day: 1,
          hour: 3,
          seq: 25,
          pvp: false,
          recordedAtMs: 0
        }
      ],
      0
    );
    expect(pve.openings.get('first-monster')?.responseSeq).toBe(23);
    expect(pve.moments.get(22)?.responseSeq).toBe(24);
  });
  test('every frame is reachable from the decision path, which matches its golden', async () => {
    const path = (await buildDecisionPath(detail))!;
    const reached = new Set(path.entries.flatMap((e) => e.frames));
    expect([...reached].sort((a, b) => a - b)).toEqual(
      stream.frames.map((f) => f.seq)
    );
    expect(path.entries.filter((e) => e.kind === 'battle')).toHaveLength(13);
    // The same Tripwire receives two enchantments. Each later move sees the
    // enchant then in effect, including across unrelated sparse updates.
    const tripwireMoves = path.entries.filter(
      (e): e is Extract<DecisionEntry, { kind: 'action' }> =>
        e.kind === 'action' && [493, 536, 689].includes(e.frames[0])
    );
    expect(tripwireMoves.map((e) => e.card?.enchant)).toEqual([
      'Fiery',
      'Fiery',
      'Turbo'
    ]);
    const actual =
      path.entries.map((e) => line(e, detail.cardNames)).join('\n') +
      '\n\n' +
      path.visits
        .map(
          (v) =>
            `D${v.day ?? '-'}H${v.hour ?? '-'} ${v.kind} ${card(v.encounter, detail.cardNames)} | ${v.actions.map((a) => a.order).join(',')} | ${v.closed ? 'not taken' : 'offered'} ${v.remaining.map((c) => card(c, detail.cardNames)).join(', ')} | gold ${v.gold ?? '?'} health ${v.health ?? '?'}${v.battleId ? ' ' + v.battleId : ''}`
        )
        .join('\n') +
      '\n';
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

describe('Frames as a reader sees them', () => {
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

  test('visits follow confirmed destinations, subchoices, pedestal transitions and reconnect baselines', async () => {
    const { visits } = (await buildDecisionPath(detail))!;
    const at = (order: number) =>
      visits.find((v) => v.actions.some((a) => a.order === order))!;
    expect(at(6)).toMatchObject({
      day: 1,
      hour: 1,
      kind: 'shop',
      gold: -6,
      rerollFees: [2],
      closed: true
    });
    const bought = at(8).actions.find((a) => a.order === 8);
    expect(bought).toMatchObject({ verb: 'bought', card: { size: 1 } });
    expect(detail.cardNames.get(bought!.card!.templateId!)).toBe(
      'Mantis Shrimp'
    );
    expect(at(6).remaining).toHaveLength(4);
    expect(at(18)).toBe(at(20));
    expect(at(159)).toBe(at(163));
    expect(at(163).kind).toBe('pedestal');
    expect(at(263).actions[0].outcome).toBe('no-response');
    expect(at(263)).not.toBe(at(268));
    expect(at(167).actions.find((a) => a.order === 167)).toMatchObject({
      amount: 1,
      gold: 6
    });
    expect(at(559).actions.find((a) => a.order === 559)).toMatchObject({
      amount: 16,
      gold: -13
    });
    expect(
      visits.flatMap((v) => (v.battleId ? [v.battleId] : []))
    ).toHaveLength(13);
  });

  test('an unreadable delta leaves offers unresolved and places activity only after the actual sync', async () => {
    const damaged = structuredClone(stream.frames);
    damaged.find((f) => f.seq === 11)!.message = new Uint8Array([0xc1]);
    const { entries, contexts } = await projectDecisions(
      new MessageStream(damaged, SCHEMA, 'desktop'),
      [],
      0
    );
    const visits = projectVisits(entries, contexts);
    expect(visits.find((v) => v.order === 7)?.closed).toBe(false);
    expect(visits.find((v) => v.actions.some((a) => a.order === 12))?.day).toBe(
      null
    );
    expect(
      visits.find((v) => v.actions.some((a) => a.order === 268))?.day
    ).toBe(5);
  });
});
