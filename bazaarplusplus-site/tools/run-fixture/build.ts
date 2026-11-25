// Builds a Run segment (RunPayloadV6, gzip) from a Run exported from a local history database,
// and by default removes the identities in it so it can be committed as a fixture.
//
//   bun tools/run-fixture/build.ts <events.json> <run.json> <history.db> <out.b64>
//       [--keep-identities] [--hashes <out.json>]
//
// events.json and run.json are `sqlite3 -json` rows of run_events and runs for one Run. Account
// ids, display names, titles, the Run id, Battle ids and the opponents' cosmetic ids are replaced
// everywhere: in the payload, the event JSON, the command arguments and inside the game's
// messages (decompressed, rewritten, compressed again with the original block split). The script
// fails if any original value is left. --hashes writes the SHA-256 of each removed id for the
// fixture test.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { gunzipSync } from 'node:zlib';

import schemaJson from '../../../runs/message-stream/game-message-schema.json';
import type { GameMessageSchema } from '../../../runs/message-stream/schema';
import {
  SchemaIndex,
  TMap,
  TObject,
  translateMessage,
  type Translated
} from '../../../runs/message-stream/translate';
import { unwrapMessage } from '../../../runs/message-stream/wire';
import { decodeMessagePackValue, MpMap } from '../../../runs/msgpack';
import { gzipRun } from '../../test/fixtures/run-payload';
import {
  rewriteStrings,
  wrapLz4BlockArray
} from '../../../runs/test/fixtures/rewrite';

export const FIXTURE_RUN_ID = '00000000-0000-4000-8000-000000000001';
export const FIXTURE_ACCOUNT_ID = '00000000-0000-4000-8000-000000000002';

type EventRow = {
  seq: number;
  ts_at_ms: number;
  kind: string;
  payload_json: string;
};

const schema = schemaJson as GameMessageSchema;
const index = new SchemaIndex(schema);

function enumValues(name: string): Map<string, number> {
  const type = Object.entries(schema.types).find(([n]) =>
    n.endsWith(`.${name}`)
  )?.[1];
  if (type?.kind !== 'enum') throw new Error(`No enum ${name}`);
  return new Map(type.values.map((v) => [v.name, Number(v.value)]));
}
const CARD_TYPE = enumValues('ECardType');
const CARD_SIZE = enumValues('ECardSize');
const SECTION = enumValues('EInventorySection');
const SOCKET = enumValues('EContainerSocketId');

/** Every string inside a translated value. */
function strings(value: Translated, out: Set<string>) {
  if (typeof value === 'string') out.add(value);
  else if (Array.isArray(value)) for (const v of value) strings(v, out);
  else if (value instanceof TObject)
    for (const v of Object.values(value.fields)) strings(v, out);
}

function findObjects(value: Translated, type: string, out: TObject[]) {
  if (Array.isArray(value)) for (const v of value) findObjects(v, type, out);
  else if (value instanceof TObject) {
    if (value.type.endsWith(`.${type}`)) out.push(value);
    for (const v of Object.values(value.fields)) findObjects(v, type, out);
  } else if (value instanceof TMap)
    for (const [, v] of value.entries) findObjects(v, type, out);
}

/**
 * Every string a Run segment holds: payload strings, the JSON inside event and command
 * strings, and the strings inside each decompressed game message.
 */
export function fixtureStrings(gz: Uint8Array): Set<string> {
  const out = new Set<string>();
  const walk = (v: unknown) => {
    if (typeof v === 'string') {
      out.add(v);
      if (v.startsWith('{') || v.startsWith('[')) {
        try {
          walk(JSON.parse(v));
        } catch {
          // Not JSON.
        }
      }
    } else if (v instanceof Uint8Array) {
      try {
        walk(decodeMessagePackValue(unwrapMessage(v).bytes));
      } catch {
        // Not a message.
      }
    } else if (Array.isArray(v)) v.forEach(walk);
    else if (v instanceof MpMap)
      v.entries.forEach(([k, x]) => (walk(k), walk(x)));
    else if (v && typeof v === 'object')
      Object.entries(v).forEach(([k, x]) => (walk(k), walk(x)));
  };
  walk(decodeMessagePackValue(new Uint8Array(gunzipSync(gz))));
  return out;
}

const n = (v: unknown) => (typeof v === 'number' ? v : null);
const str = (v: unknown) => (typeof v === 'string' ? v : null);

function main() {
  const args = process.argv.slice(2);
  const keep = args.includes('--keep-identities');
  const hashesAt = args.indexOf('--hashes');
  const hashesPath = hashesAt >= 0 ? args[hashesAt + 1] : null;
  const [eventsPath, runPath, dbPath, outPath] = args.filter(
    (a, i) => !a.startsWith('--') && (hashesAt < 0 || i !== hashesAt + 1)
  );
  const events = JSON.parse(fs.readFileSync(eventsPath, 'utf8')) as EventRow[];
  const [run] = JSON.parse(fs.readFileSync(runPath, 'utf8')) as Array<
    Record<string, unknown>
  >;
  const runId = String(run.run_id);
  if (!/^[\w-]+$/.test(runId)) throw new Error('Unexpected run id');
  const battles = JSON.parse(
    execFileSync(
      'sqlite3',
      [
        '-readonly',
        '-json',
        dbPath,
        `select b.*, s.player_hand_json, s.player_skills_json, s.opponent_hand_json, s.opponent_skills_json
         from battles b left join battle_snapshots s using (battle_id)
         where b.run_id = '${runId}' order by b.recorded_at_ms`
      ],
      { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 }
    ) || '[]'
  ) as Array<Record<string, unknown>>;

  // --- The values to replace, each with its placeholder.
  const replace = new Map<string, string>();
  const ids = new Set<string>();
  const put = (original: unknown, placeholder: string, id = true) => {
    if (
      typeof original !== 'string' ||
      original === '' ||
      replace.has(original)
    )
      return;
    replace.set(original, placeholder);
    if (id) ids.add(original);
  };
  const names = new Map<string, number>();
  const nameFor = (name: unknown) => {
    if (typeof name !== 'string' || name === '' || replace.has(name)) return;
    names.set(name, names.size + 1);
    put(name, `Opponent ${names.size}`, false);
  };
  put(runId, FIXTURE_RUN_ID);
  put(run.player_account_id, FIXTURE_ACCOUNT_ID);
  let uuid = 16;
  const placeholderUuid = () =>
    `00000000-0000-4000-8000-${(uuid++).toString(16).padStart(12, '0')}`;
  /** An id and its hyphenless form (a board override names the board that way). */
  const putId = (id: string) => {
    if (replace.has(id)) return;
    const placeholder = placeholderUuid();
    put(id, placeholder);
    if (id.includes('-'))
      put(id.replaceAll('-', ''), placeholder.replaceAll('-', ''));
  };
  battles.forEach((b, i) => {
    put(b.battle_id, `battle-${String(i + 1).padStart(2, '0')}`);
    put(b.player_account_id, FIXTURE_ACCOUNT_ID);
    put(b.player_name, 'Player', false);
    put(b.opponent_account_id, placeholderUuid());
    nameFor(b.opponent_name);
  });

  const frames = events.filter((e) => e.kind.startsWith('stream_'));
  for (const e of frames) {
    const body = JSON.parse(e.payload_json) as { message?: string };
    if (!body.message) continue;
    const { value } = translateMessage(
      index,
      decodeMessagePackValue(
        unwrapMessage(Buffer.from(body.message, 'base64')).bytes
      )
    );
    const opponents: TObject[] = [];
    findObjects(value, 'SimPvpOpponent', opponents);
    for (const opponent of opponents) {
      nameFor(opponent.fields.Name);
      for (const title of [
        opponent.fields.TitlePrefix,
        opponent.fields.TitleSuffix
      ])
        put(title, 'Title', false);
      const found = new Set<string>();
      strings(opponent.fields.PlayerLoadout, found);
      strings(opponent.fields.PlayerCollection, found);
      for (const id of found) putId(id);
    }
    // A completed Run's rewards hold the player's own chest and item ids.
    const rewards: TObject[] = [];
    findObjects(value, 'CompleteRunResult', rewards);
    for (const reward of rewards) {
      const found = new Set<string>();
      strings(reward, found);
      for (const id of found) putId(id);
    }
    const initialized: TObject[] = [];
    findObjects(value, 'NetMessageRunInitialized', initialized);
    for (const m of initialized) put(m.fields.RunId, FIXTURE_RUN_ID);
  }
  if (keep) replace.clear();

  const swap = (v: unknown): unknown =>
    typeof v === 'string' ? (replace.get(v) ?? v) : v;
  const swapJson = (v: unknown): unknown =>
    Array.isArray(v)
      ? v.map(swapJson)
      : v && typeof v === 'object'
        ? Object.fromEntries(
            Object.entries(v).map(([k, x]) => [k, swapJson(x)])
          )
        : swap(v);

  // --- Key 8 frames and key 4 events, as RunPayloadComposer maps them.
  const STRIPPED = new Set(['run_id', 'seq', 'ts', 'kind']);
  const stream = frames.map((e) => {
    const body = JSON.parse(e.payload_json) as {
      message?: string;
      responds_to_seq?: number;
      command?: string;
      command_args?: unknown;
    };
    let message: Uint8Array | null = null;
    if (body.message) {
      const original = new Uint8Array(Buffer.from(body.message, 'base64'));
      const wire = unwrapMessage(original);
      const rewritten = rewriteStrings(wire.bytes, replace);
      message =
        wire.blocks.length === 0
          ? rewritten
          : wrapLz4BlockArray(rewritten, wire.blocks.slice(0, -1));
    }
    return [
      e.seq,
      e.ts_at_ms,
      message,
      body.responds_to_seq ?? null,
      body.command ?? null,
      body.command_args === undefined
        ? null
        : JSON.stringify(swapJson(body.command_args))
    ];
  });
  const runEvents = events
    .filter((e) => !e.kind.startsWith('stream_'))
    .map((e) => {
      const body = JSON.parse(e.payload_json) as Record<string, unknown>;
      const kept = Object.fromEntries(
        Object.entries(body)
          .filter(([k]) => !STRIPPED.has(k))
          .map(([k, v]) => [k, swapJson(v)])
      );
      return [e.seq, e.ts_at_ms, e.kind, JSON.stringify(kept)];
    });

  const started = events.find((e) => e.kind === 'run_started');
  const startedBody = started
    ? (JSON.parse(started.payload_json) as Record<string, unknown>)
    : {};
  const card = (c: Record<string, unknown>) => [
    c.instance_id,
    c.template_id,
    CARD_TYPE.get(String(c.type)) ?? 0,
    CARD_SIZE.get(String(c.size)) ?? 1,
    c.section == null ? null : (SECTION.get(str(c.section) ?? '') ?? null),
    c.socket == null ? null : (SOCKET.get(str(c.socket) ?? '') ?? null),
    str(c.name),
    str(c.tier),
    str(c.enchant),
    Array.isArray(c.tags) ? c.tags : [],
    (c.attributes as Record<string, number>) ?? {}
  ];
  const set = (label: string, json: unknown) => {
    if (typeof json !== 'string') return [label, 'Missing', null, []];
    const doc = JSON.parse(json) as {
      items?: Array<Record<string, unknown>>;
      status?: string;
      source?: string;
    };
    const items = doc.items ?? [];
    return [
      label,
      doc.status ?? (items.length ? 'Captured' : 'CapturedEmpty'),
      doc.source ?? null,
      items.map(card)
    ];
  };
  const participant = (
    b: Record<string, unknown>,
    side: 'player' | 'opponent'
  ) => [
    swap(b[`${side}_account_id`]),
    swap(b[`${side}_name`]),
    b[`${side}_hero`],
    b[`${side}_rank`],
    n(b[`${side}_rating`]),
    n(b[`${side}_level`]),
    n(b[`${side}_prestige`]),
    n(b[`${side}_victories`]),
    side === 'player' ? n(b.player_income) : null,
    side === 'player' ? n(b.player_gold) : null,
    n(b[`${side}_hand_item_count`]),
    n(b[`${side}_skill_count`])
  ];
  const payload = [
    6,
    swap(runId),
    swap(run.player_account_id),
    [
      run.hero,
      run.game_mode,
      null,
      run.started_at_ms,
      run.ended_at_ms ?? null,
      run.status,
      n(run.final_day ?? run.day),
      n(run.final_hour ?? run.hour),
      n(run.victories),
      n(run.losses),
      str(run.player_rank),
      n(run.player_rating),
      str(run.final_player_rank),
      n(run.final_player_rating),
      n(run.final_player_rating_delta),
      n(run.max_health),
      n(run.prestige),
      n(run.level),
      n(run.income),
      n(run.gold),
      str(run.build_channel),
      str(run.mod_version) ?? '',
      str(startedBody.server_build_id),
      str(startedBody.game_data_etag),
      str(run.mod_version) ?? '',
      'desktop'
    ],
    runEvents,
    battles.map((b) => [
      swap(b.battle_id),
      [
        b.recorded_at_ms,
        n(b.day),
        n(b.hour),
        str(b.encounter_id),
        String(b.combat_kind),
        str(b.result),
        str(b.winner_combatant_id),
        str(b.loser_combatant_id),
        Boolean(b.is_final_battle)
      ],
      [participant(b, 'player'), participant(b, 'opponent')],
      b.player_hand_json == null
        ? null
        : [
            [
              set('player_hand', b.player_hand_json),
              set('player_skills', b.player_skills_json),
              set('opponent_hand', b.opponent_hand_json),
              set('opponent_skills', b.opponent_skills_json)
            ]
          ],
      null
    ]),
    [],
    [[], [], 0, false],
    stream
  ];
  const gz = gzipRun(payload);

  if (!keep) {
    const left = [...fixtureStrings(gz)].filter((text) => replace.has(text));
    if (left.length)
      throw new Error(`${left.length} removed values are left in the fixture`);
  }
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, Buffer.from(gz).toString('base64') + '\n');
  if (hashesPath)
    fs.writeFileSync(
      hashesPath,
      JSON.stringify(
        [...ids]
          .map((id) => createHash('sha256').update(id).digest('hex'))
          .sort(),
        null,
        2
      ) + '\n'
    );
  process.stderr.write(
    `Wrote ${outPath}: ${stream.length} frames, ${battles.length} battles, ${replace.size} values replaced\n`
  );
}

if (import.meta.main) main();
