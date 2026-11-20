import { Buffer } from 'node:buffer';
import { canonicalHero } from './commands';
import { MessagePack } from './msgpack';
import type { Battle, BundleRef, Card, Run } from './model';

function slots(value: unknown, size: number): unknown[] {
  if (!Array.isArray(value) || value.length < size)
    throw new Error('Invalid Run array');
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
  if (typeof value !== 'number' || !Number.isSafeInteger(value))
    throw new Error('Invalid Run integer');
  return value;
}
function number(value: unknown): number | null {
  return value === null ? null : integer(value);
}
function cards(value: unknown): Card[] | null {
  if (value === null) return null;
  const sets = slots(slots(value, 1)[0], 0)
    .map((v) => slots(v, 4))
    .filter((s) => s[0] === 'player_hand');
  if (sets.length !== 1 || sets[0][1] !== 'Captured') return null;
  return slots(sets[0][3], 0)
    .map((v) => slots(v, 11))
    .filter((c) => c[2] === 0)
    .map((c) => ({
      id: text(c[1]).toLowerCase(),
      name: optionalText(c[6]) ?? text(c[1]),
      enchant:
        !optionalText(c[8])?.trim() ||
        optionalText(c[8])?.trim().toLowerCase() === 'none'
          ? null
          : optionalText(c[8])!.trim(),
      size: integer(c[3]),
      slot: number(c[5]),
      tier: optionalText(c[7])
    }))
    .sort((a, b) => (a.slot ?? 0) - (b.slot ?? 0));
}
function battle(value: unknown): Battle {
  const b = slots(value, 5);
  const f = slots(b[1], 9);
  const p = slots(b[2], 2);
  const player = slots(p[0], 12);
  const opponent = slots(p[1], 12);
  const winner = optionalText(f[6]);
  const hero = optionalText(opponent[2]);
  if (typeof f[8] !== 'boolean') throw new Error('Invalid final Battle flag');
  return {
    id: text(b[0]),
    at: integer(f[0]),
    day: number(f[1]),
    // The mod records PVPCombat; a Ghost import records pvp.
    pvp: text(f[4]).trim().toLowerCase().startsWith('pvp'),
    final: f[8],
    opponent: hero === null ? null : canonicalHero(hero),
    winner:
      winner !== null && (winner === 'Player' || winner === player[0])
        ? 'player'
        : winner !== null && (winner === 'Opponent' || winner === opponent[0])
          ? 'opponent'
          : null,
    cards: cards(b[3])
  };
}
export async function decodeRun(
  payload: Uint8Array,
  runId: string,
  accountId: string
): Promise<Run> {
  if (payload[0] !== 0x1f || payload[1] !== 0x8b)
    throw new Error('Run is not gzip');
  const stream = new Response(new Uint8Array(payload).buffer).body!.pipeThrough(
    new DecompressionStream('gzip')
  );
  const root = slots(await new MessagePack(stream).decode(), 8);
  if (root[0] !== 6 || root[1] !== runId || root[2] !== accountId)
    throw new Error('Run identity/version mismatch');
  const r = slots(root[3], 22);
  return {
    id: text(root[1]),
    account: text(root[2]),
    hero: canonicalHero(text(r[0])),
    status: text(r[5]).trim().toLowerCase(),
    ended: number(r[4]),
    rank: optionalText(r[12])?.trim() ?? null,
    day: number(r[6]),
    wins: number(r[8]),
    losses: number(r[9]),
    initialRating: number(r[11]),
    rating: number(r[13]),
    delta: number(r[14]),
    battles: slots(root[5], 0)
      .map(battle)
      .sort((a, b) => a.at - b.at || a.id.localeCompare(b.id))
  };
}
export async function digest(bytes: Uint8Array): Promise<string> {
  return Buffer.from(
    await crypto.subtle.digest('SHA-256', new Uint8Array(bytes).buffer)
  ).toString('hex');
}
export async function readBounded(
  response: Response,
  limit: number
): Promise<Uint8Array> {
  if (!response.ok || !response.body)
    throw new Error(`Download failed (${response.status})`);
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;
  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      length += value.length;
      if (length > limit) throw new Error('Download exceeds limit');
      chunks.push(value);
    }
  } finally {
    await reader.cancel();
  }
  const result = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    result.set(chunk, offset);
    offset += chunk.length;
  }
  return result;
}
interface Segment {
  offset: number;
  length: number;
  sha256: string;
  content_type: string;
}
interface Manifest {
  bundle_id: string;
  bundle_version: number;
  run: {
    run_id: string;
    player_account_id: string;
    run_format_version: number;
    payload: Segment;
  };
  screenshot?: Segment;
}
export async function decodeBundle(
  bytes: Uint8Array,
  ref: BundleRef,
  account: string
): Promise<Run> {
  if (
    bytes.length < 16 ||
    bytes.length > 8_388_607 ||
    (await digest(bytes)) !== ref.sha256
  )
    throw new Error('Bundle digest/size mismatch');
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (
    new TextDecoder().decode(bytes.subarray(0, 8)) !== 'BPPBNDL6' ||
    view.getUint32(8) !== 6
  )
    throw new Error('Invalid Bundle prefix');
  const length = view.getUint32(12);
  if (length < 1 || length > 2097152 || 16 + length > bytes.length)
    throw new Error('Invalid manifest size');
  const m = JSON.parse(
    new TextDecoder('utf-8', { fatal: true }).decode(
      bytes.subarray(16, 16 + length)
    )
  ) as Manifest;
  if (
    m.bundle_version !== 6 ||
    m.bundle_id !== ref.bundle_id ||
    m.run.run_format_version !== 6 ||
    m.run.run_id !== ref.run_id ||
    m.run.player_account_id !== account
  )
    throw new Error('Bundle identity/version mismatch');
  const payload = m.run.payload;
  if (
    payload.length > 2097151 ||
    payload.offset !== 0 ||
    payload.content_type !== 'application/x-bpp-run-v6'
  )
    throw new Error('Invalid Run segment');
  const segment = async (s: Segment) => {
    if (
      !Number.isSafeInteger(s.length) ||
      s.length < 1 ||
      !Number.isSafeInteger(s.offset) ||
      s.offset < 0 ||
      16 + length + s.offset + s.length > bytes.length
    )
      throw new Error('Invalid segment bounds');
    const data = bytes.subarray(
      16 + length + s.offset,
      16 + length + s.offset + s.length
    );
    if ((await digest(data)) !== s.sha256)
      throw new Error('Segment digest mismatch');
    return data;
  };
  const runBytes = await segment(payload);
  // The Screenshot is not drawn, but it is part of the Bundle's integrity.
  if (m.screenshot) {
    if (
      m.screenshot.length > 1048576 ||
      m.screenshot.offset !== payload.length ||
      !['image/jpeg', 'image/webp'].includes(m.screenshot.content_type)
    )
      throw new Error('Invalid Screenshot');
    await segment(m.screenshot);
  }
  if (
    16 + length + payload.length + (m.screenshot?.length ?? 0) !==
    bytes.length
  )
    throw new Error('Trailing Bundle bytes');
  return decodeRun(runBytes, ref.run_id, account);
}
