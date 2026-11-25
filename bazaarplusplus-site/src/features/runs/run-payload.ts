import type { CardSize } from '@bpp/ui';
import type { StreamFrame } from './message-stream/stream';
import { decodeMessagePack } from './msgpack';

/** The Run segment layout is bazaarplusplus-mod/docs/contracts/run-payload-v6.md. */
const PAYLOAD_FORMAT_VERSION = 6;
const MAX_DECOMPRESSED_BYTES = 64 * 1024 * 1024;

export type CardSetStatus = 'captured' | 'empty' | 'missing';

export type RunCard = {
  instanceId: string;
  templateId: string;
  /** The game's card type; 0 is an item. */
  type: number;
  /** Board width in slots: 1 small, 2 medium, 3 large. */
  size: CardSize;
  /** First board slot, 0–9. */
  slot: number | null;
  name: string | null;
  tier: string | null;
  enchant: string | null;
};

export type CardSet = {
  status: CardSetStatus;
  cards: RunCard[];
};

export type RunBattle = {
  id: string;
  /** `pve-event`: a PvE fight known only from its `pve_combat_recorded` event, with no board. */
  source: 'battle' | 'pve-event';
  recordedAtMs: number;
  day: number | null;
  hour: number | null;
  encounterId: string | null;
  pvp: boolean;
  outcome: 'win' | 'loss' | null;
  opponentHero: string | null;
  hand: CardSet;
  skills: CardSet;
};

export type RunDetail = {
  runId: string;
  hero: string;
  /** In time order. */
  battles: RunBattle[];
  /** Run Message Stream frames (key 8) in Seq order; null when the Run has none. */
  stream: StreamFrame[] | null;
  /** The size budget dropped the stream (`message_stream_omitted`). */
  streamOmitted: boolean;
  /** RunFactsV6 key 25: `desktop` or `android`, null when not recorded. */
  platform: string | null;
  /** The Seq of each Battle's `pvp_combat_recorded` event, by Battle id. */
  battleSeqs: Map<string, number>;
  /** Card names by TemplateId, from every card set of the Run's Battles. */
  cardNames: Map<string, string>;
};

const MISSING: CardSet = { status: 'missing', cards: [] };

function fields(value: unknown, minimum: number): unknown[] {
  if (!Array.isArray(value) || value.length < minimum) {
    throw new Error('Invalid Run array');
  }
  return value;
}

function text(value: unknown): string {
  if (typeof value !== 'string') throw new Error('Invalid Run string');
  return value;
}

function optionalText(value: unknown): string | null {
  return value == null ? null : text(value);
}

function integer(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value)) {
    throw new Error('Invalid Run integer');
  }
  return value;
}

function optionalInteger(value: unknown): number | null {
  return value == null ? null : integer(value);
}

function card(value: unknown): RunCard {
  const c = fields(value, 9);
  const size = integer(c[3]);
  const enchant = optionalText(c[8])?.trim() ?? '';
  return {
    instanceId: text(c[0]),
    templateId: text(c[1]).toLowerCase(),
    type: integer(c[2]),
    size: size <= 1 ? 1 : size === 2 ? 2 : 3,
    slot: optionalInteger(c[5]),
    name: optionalText(c[6]),
    tier: optionalText(c[7]),
    enchant: enchant && enchant.toLowerCase() !== 'none' ? enchant : null
  };
}

function cardSet(sets: unknown[] | null, label: string): CardSet {
  const matches = (sets ?? [])
    .map((set) => fields(set, 4))
    .filter((set) => set[0] === label);
  if (matches.length !== 1) return MISSING;
  const [, status, , cards] = matches[0];
  if (status === 'CapturedEmpty') return { status: 'empty', cards: [] };
  if (status !== 'Captured') return MISSING;
  return { status: 'captured', cards: fields(cards, 0).map(card) };
}

function battle(value: unknown): RunBattle {
  const b = fields(value, 4);
  const facts = fields(b[1], 9);
  const [player, opponent] = fields(b[2], 2).map((side) => fields(side, 3));
  const sets = b[3] == null ? null : fields(fields(b[3], 1)[0], 0);
  const winner = optionalText(facts[6]);
  const result = optionalText(facts[5])?.trim().toLowerCase();
  const outcome =
    winner !== null && (winner === 'Player' || winner === player[0])
      ? 'win'
      : winner !== null && (winner === 'Opponent' || winner === opponent[0])
        ? 'loss'
        : result === 'win' || result === 'loss'
          ? result
          : null;
  return {
    id: text(b[0]),
    source: 'battle',
    recordedAtMs: integer(facts[0]),
    day: optionalInteger(facts[1]),
    hour: optionalInteger(facts[2]),
    encounterId: optionalText(facts[3]),
    // The mod records PVPCombat; a Ghost import records pvp.
    pvp: text(facts[4]).trim().toLowerCase().startsWith('pvp'),
    outcome,
    opponentHero: optionalText(opponent[2]),
    hand: cardSet(sets, 'player_hand'),
    skills: cardSet(sets, 'player_skills')
  };
}

function pveEvent(value: unknown): RunBattle | null {
  const event = fields(value, 4);
  if (event[2] !== 'pve_combat_recorded' || typeof event[3] !== 'string') {
    return null;
  }
  let body: Record<string, unknown>;
  try {
    body = JSON.parse(event[3]) as Record<string, unknown>;
  } catch {
    return null;
  }
  const number = (key: string) =>
    typeof body[key] === 'number' && Number.isSafeInteger(body[key])
      ? body[key]
      : null;
  return {
    id: `event-${integer(event[0])}`,
    source: 'pve-event',
    recordedAtMs: integer(event[1]),
    day: number('day'),
    hour: number('hour'),
    encounterId:
      typeof body.encounter_id === 'string' ? body.encounter_id : null,
    pvp: false,
    outcome:
      body.winner === 'Player'
        ? 'win'
        : body.loser === 'Player'
          ? 'loss'
          : null,
    opponentHero: null,
    hand: MISSING,
    skills: MISSING
  };
}

/** A captured monster-combat Battle already shows the fight its event describes. */
function sameFight(event: RunBattle, captured: RunBattle): boolean {
  return (
    !captured.pvp &&
    captured.day === event.day &&
    captured.hour === event.hour &&
    (captured.encounterId === null ||
      event.encounterId === null ||
      captured.encounterId === event.encounterId)
  );
}

async function gunzip(bytes: Uint8Array): Promise<Uint8Array> {
  if (bytes[0] !== 0x1f || bytes[1] !== 0x8b) {
    throw new Error('Run is not gzip');
  }
  const reader = new Response(new Uint8Array(bytes).buffer)
    .body!.pipeThrough(new DecompressionStream('gzip'))
    .getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;
  try {
    for (;;) {
      // oxlint-disable-next-line no-await-in-loop -- a stream reads in order.
      const { value, done } = await reader.read();
      if (done) break;
      length += value.length;
      if (length > MAX_DECOMPRESSED_BYTES) {
        throw new Error('Run exceeds 64 MiB');
      }
      chunks.push(value);
    }
  } finally {
    await reader.cancel().catch(() => undefined);
  }
  const out = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    out.set(chunk, offset);
    offset += chunk.length;
  }
  return out;
}

/** Root keys left unbuilt: replayable ids. */
const SKIPPED_ROOT_KEYS = new Set([6]);

function streamFrame(value: unknown): StreamFrame {
  const f = fields(value, 6);
  if (f[2] !== null && !(f[2] instanceof Uint8Array)) {
    throw new Error('Invalid Run frame message');
  }
  return {
    seq: integer(f[0]),
    timestampMs: integer(f[1]),
    message: f[2],
    respondsToSeq: optionalInteger(f[3]),
    command: optionalText(f[4]),
    commandArgsJson: optionalText(f[5])
  };
}

function battleSeq(value: unknown): [string, number] | null {
  const event = fields(value, 4);
  if (event[2] !== 'pvp_combat_recorded' || typeof event[3] !== 'string') {
    return null;
  }
  try {
    const body = JSON.parse(event[3]) as { battle_id?: unknown };
    return typeof body.battle_id === 'string'
      ? [body.battle_id, integer(event[0])]
      : null;
  } catch {
    return null;
  }
}

function cardNames(battles: unknown[]): Map<string, string> {
  const names = new Map<string, string>();
  for (const b of battles) {
    const snapshots = fields(b, 4)[3];
    if (snapshots == null) continue;
    for (const set of fields(fields(snapshots, 1)[0], 0)) {
      const cards = fields(set, 4)[3];
      for (const c of fields(cards, 0)) {
        const values = fields(c, 9);
        if (typeof values[1] === 'string' && typeof values[6] === 'string') {
          names.set(values[1].toLowerCase(), values[6]);
        }
      }
    }
  }
  return names;
}

export async function decodeRunPayload(
  bytes: Uint8Array,
  runId: string
): Promise<RunDetail> {
  const root = fields(
    decodeMessagePack(
      await gunzip(bytes),
      (path) =>
        (path.length === 1 && SKIPPED_ROOT_KEYS.has(path[0])) ||
        // Battle replays: native Spawn, Combat and Despawn bytes and the stash.
        (path.length === 3 && path[0] === 5 && path[2] === 4)
    ),
    6
  );
  if (root[0] !== PAYLOAD_FORMAT_VERSION) {
    throw new Error('Unsupported Run payload version');
  }
  if (root[1] !== runId) {
    throw new Error('Run id mismatch');
  }
  const rawBattles = fields(root[5], 0);
  const battles = rawBattles.map(battle);
  const events = root[4] == null ? [] : fields(root[4], 0);
  const facts = fields(root[3], 1);
  const degradation = root[7] == null ? null : fields(root[7], 1);
  const pve = events
    .map(pveEvent)
    .filter((event): event is RunBattle => event !== null)
    .filter((event) => !battles.some((b) => sameFight(event, b)));
  return {
    runId,
    hero: text(facts[0]),
    battles: [...battles, ...pve].sort(
      (a, b) => a.recordedAtMs - b.recordedAtMs || a.id.localeCompare(b.id)
    ),
    stream:
      root[8] == null
        ? null
        : fields(root[8], 0)
            .map(streamFrame)
            .sort((a, b) => a.seq - b.seq),
    streamOmitted:
      degradation !== null &&
      fields(degradation[0], 0).includes('message_stream_omitted'),
    platform: facts.length > 25 ? optionalText(facts[25]) : null,
    battleSeqs: new Map(
      events.map(battleSeq).filter((e): e is [string, number] => e !== null)
    ),
    cardNames: cardNames(rawBattles)
  };
}
