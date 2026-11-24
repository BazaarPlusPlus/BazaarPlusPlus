/** Decides, by its position in the tree, whether a value is skipped instead of built. */
export type SkipRule = (path: readonly number[]) => boolean;

const MAX_DEPTH = 64;
const MAX_SAFE = BigInt(Number.MAX_SAFE_INTEGER);
const MIN_SAFE = BigInt(Number.MIN_SAFE_INTEGER);

/** A map that keeps its entries in order and its keys with their own types. */
export class MpMap {
  readonly entries: Array<[MpValue, MpValue]>;
  constructor(entries: Array<[MpValue, MpValue]>) {
    this.entries = entries;
  }
}

/** An extension value: its type code and its bytes. */
export class MpExt {
  readonly type: number;
  readonly data: Uint8Array;
  constructor(type: number, data: Uint8Array) {
    this.type = type;
    this.data = data;
  }
}

/**
 * A lossless MessagePack value. An integer outside the safe Number range is a bigint; a binary
 * is a view into the decoded bytes.
 */
export type MpValue =
  | null
  | boolean
  | number
  | bigint
  | string
  | Uint8Array
  | MpValue[]
  | MpMap
  | MpExt;

type Mode = { lossless: boolean; skip: SkipRule };

function read(bytes: Uint8Array, { lossless, skip }: Mode): unknown {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const text = new TextDecoder('utf-8', { fatal: true });
  let offset = 0;

  function take(count: number): number {
    if (count < 0 || offset + count > bytes.length) {
      throw new Error('Truncated MessagePack');
    }
    const start = offset;
    offset += count;
    return start;
  }

  function length(size: 1 | 2 | 4): number {
    const at = take(size);
    return size === 1
      ? view.getUint8(at)
      : size === 2
        ? view.getUint16(at)
        : view.getUint32(at);
  }

  function integer(size: 1 | 2 | 4 | 8, signed: boolean): number | bigint {
    const at = take(size);
    if (size < 8) {
      return size === 1
        ? signed
          ? view.getInt8(at)
          : view.getUint8(at)
        : size === 2
          ? signed
            ? view.getInt16(at)
            : view.getUint16(at)
          : signed
            ? view.getInt32(at)
            : view.getUint32(at);
    }
    // Compare as a bigint first: converting an unsafe value to a Number would round it.
    const wide = signed ? view.getBigInt64(at) : view.getBigUint64(at);
    if (wide <= MAX_SAFE && wide >= MIN_SAFE) return Number(wide);
    if (lossless) return wide;
    throw new Error('Unsafe MessagePack integer');
  }

  function value(path: number[], keep: boolean): unknown {
    if (path.length > MAX_DEPTH) {
      throw new Error('MessagePack nesting too deep');
    }
    const marker = view.getUint8(take(1));
    if (marker < 0x80) return marker;
    if (marker >= 0xe0) return marker - 0x100;
    if (marker === 0xc0) return null;
    if (marker === 0xc2) return false;
    if (marker === 0xc3) return true;
    if (marker >= 0xcc && marker <= 0xcf) {
      return integer((1 << (marker - 0xcc)) as 1 | 2 | 4 | 8, false);
    }
    if (marker >= 0xd0 && marker <= 0xd3) {
      return integer((1 << (marker - 0xd0)) as 1 | 2 | 4 | 8, true);
    }
    if (marker === 0xca) return view.getFloat32(take(4));
    if (marker === 0xcb) return view.getFloat64(take(8));

    const fixString = marker >= 0xa0 && marker <= 0xbf;
    if (fixString || (marker >= 0xd9 && marker <= 0xdb)) {
      const size = fixString
        ? marker & 0x1f
        : length((1 << (marker - 0xd9)) as 1 | 2 | 4);
      const at = take(size);
      return keep ? text.decode(bytes.subarray(at, at + size)) : null;
    }
    if (marker >= 0xc4 && marker <= 0xc6) {
      const size = length((1 << (marker - 0xc4)) as 1 | 2 | 4);
      const at = take(size);
      return keep ? bytes.subarray(at, at + size) : null;
    }
    // fixext 1/2/4/8/16, then ext 8/16/32.
    if (
      (marker >= 0xd4 && marker <= 0xd8) ||
      (marker >= 0xc7 && marker <= 0xc9)
    ) {
      const size =
        marker >= 0xd4
          ? 1 << (marker - 0xd4)
          : length((1 << (marker - 0xc7)) as 1 | 2 | 4);
      const type = view.getInt8(take(1));
      const at = take(size);
      if (!lossless) throw new Error('Unexpected MessagePack extension');
      return keep ? new MpExt(type, bytes.subarray(at, at + size)) : null;
    }

    const fixArray = marker >= 0x90 && marker <= 0x9f;
    if (fixArray || marker === 0xdc || marker === 0xdd) {
      const count = fixArray ? marker & 0x0f : length(marker === 0xdc ? 2 : 4);
      if (count > bytes.length - offset) {
        throw new Error('Invalid MessagePack array');
      }
      const values: unknown[] = [];
      for (let index = 0; index < count; index++) {
        const child = [...path, index];
        const item = value(child, keep && !skip(child));
        if (keep) values.push(item);
      }
      return keep ? values : null;
    }

    const fixMap = marker >= 0x80 && marker <= 0x8f;
    if (fixMap || marker === 0xde || marker === 0xdf) {
      const count = fixMap ? marker & 0x0f : length(marker === 0xde ? 2 : 4);
      if (count * 2 > bytes.length - offset) {
        throw new Error('Invalid MessagePack map');
      }
      if (lossless) {
        const entries: Array<[MpValue, MpValue]> = [];
        for (let index = 0; index < count; index++) {
          const key = value([...path, index], keep) as MpValue;
          const item = value([...path, index], keep) as MpValue;
          if (keep) entries.push([key, item]);
        }
        return keep ? new MpMap(entries) : null;
      }
      const values: Record<string, unknown> = Object.create(null);
      for (let index = 0; index < count; index++) {
        const key = value(path, keep);
        const item = value(path, keep);
        if (!keep) continue;
        if (typeof key !== 'string' && typeof key !== 'number') {
          throw new Error('Invalid MessagePack map key');
        }
        values[String(key)] = item;
      }
      return keep ? values : null;
    }
    throw new Error(`Unsupported MessagePack marker 0x${marker.toString(16)}`);
  }

  const result = value([], true);
  if (offset !== bytes.length) {
    throw new Error('Trailing MessagePack bytes');
  }
  return result;
}

/**
 * A small MessagePack reader for the Run segment: maps (as records), arrays, strings, binaries
 * (as views into `bytes`), integers, floats, booleans and nil. Skipped subtrees are walked but
 * never built.
 */
export function decodeMessagePack(
  bytes: Uint8Array,
  skip: SkipRule = () => false
): unknown {
  return read(bytes, { lossless: false, skip });
}

/** Reads every MessagePack value without loss, for the game's own messages. */
export function decodeMessagePackValue(bytes: Uint8Array): MpValue {
  return read(bytes, { lossless: true, skip: () => false }) as MpValue;
}
