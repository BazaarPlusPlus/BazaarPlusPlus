/** Encodes the subset of MessagePack the Run segment uses, for building test payloads. */
export function encodeMessagePack(root: unknown): Uint8Array {
  const out: number[] = [];
  const push = (...bytes: number[]) => {
    out.push(...bytes);
  };
  const head = (marker: number, length: number, size: number) => {
    push(marker);
    for (let shift = (size - 1) * 8; shift >= 0; shift -= 8) {
      push(Number((BigInt(length) >> BigInt(shift)) & 0xffn));
    }
  };
  const integer = (v: number) => {
    if (v >= 0 && v < 0x80) push(v);
    else if (v < 0 && v >= -32) push(v + 0x100);
    else if (v < 0) head(0xd3, Number(BigInt.asUintN(64, BigInt(v))), 8);
    else if (v <= 0xff) head(0xcc, v, 1);
    else if (v <= 0xffff) head(0xcd, v, 2);
    else if (v <= 0xffffffff) head(0xce, v, 4);
    else head(0xcf, v, 8);
  };
  const write = (v: unknown): void => {
    if (v === null || v === undefined) push(0xc0);
    else if (typeof v === 'boolean') push(v ? 0xc3 : 0xc2);
    else if (typeof v === 'number') {
      if (!Number.isInteger(v)) throw new Error('Floats are not used');
      integer(v);
    } else if (typeof v === 'string') {
      const bytes = new TextEncoder().encode(v);
      if (bytes.length < 32) push(0xa0 | bytes.length);
      else head(0xda, bytes.length, 2);
      push(...bytes);
    } else if (v instanceof Uint8Array) {
      head(0xc5, v.length, 2);
      push(...v);
    } else if (v instanceof Map) {
      // A Map keeps non-string keys, as the game's dictionaries need.
      if (v.size < 16) push(0x80 | v.size);
      else head(0xde, v.size, 2);
      for (const [key, item] of v) {
        write(key);
        write(item);
      }
    } else if (Array.isArray(v)) {
      if (v.length < 16) push(0x90 | v.length);
      else head(0xdc, v.length, 2);
      v.forEach(write);
    } else if (typeof v === 'object') {
      const entries = Object.entries(v);
      if (entries.length < 16) push(0x80 | entries.length);
      else head(0xde, entries.length, 2);
      for (const [key, item] of entries) {
        write(key);
        write(item);
      }
    } else throw new Error(`Cannot encode ${typeof v}`);
  };
  write(root);
  return Uint8Array.from(out);
}
