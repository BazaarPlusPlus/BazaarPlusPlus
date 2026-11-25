// Byte-level helpers for building Run Message Stream fixtures. Not shipped.

/**
 * Copies MessagePack bytes, replacing each string value found in `replace`. Every other byte is
 * kept as it was (integer widths, float32 values, extension payloads).
 */
export function rewriteStrings(
  bytes: Uint8Array,
  replace: ReadonlyMap<string, string>
): Uint8Array {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const out: number[] = [];
  const decoder = new TextDecoder();
  const encoder = new TextEncoder();
  let at = 0;
  const copy = (count: number) => {
    for (let i = 0; i < count; i++) out.push(bytes[at + i]);
    at += count;
  };
  const size = (width: 1 | 2 | 4, offset: number) =>
    width === 1
      ? view.getUint8(offset)
      : width === 2
        ? view.getUint16(offset)
        : view.getUint32(offset);

  function value(): void {
    const marker = bytes[at];
    if (marker === undefined) throw new Error('Truncated MessagePack');
    if (
      marker < 0x80 ||
      marker >= 0xe0 ||
      marker === 0xc0 ||
      marker === 0xc2 ||
      marker === 0xc3
    )
      return copy(1);
    if (marker >= 0xcc && marker <= 0xd3)
      return copy(1 + (1 << ((marker - 0xcc) & 3)));
    if (marker === 0xca) return copy(5);
    if (marker === 0xcb) return copy(9);
    const fixString = marker >= 0xa0 && marker <= 0xbf;
    if (fixString || (marker >= 0xd9 && marker <= 0xdb)) {
      const width = fixString ? 0 : ((1 << (marker - 0xd9)) as 1 | 2 | 4);
      const length = fixString
        ? marker & 0x1f
        : size(width as 1 | 2 | 4, at + 1);
      const start = at + 1 + width;
      const text = decoder.decode(bytes.subarray(start, start + length));
      const replacement = replace.get(text);
      if (replacement === undefined) return copy(1 + width + length);
      at = start + length;
      const encoded = encoder.encode(replacement);
      if (encoded.length < 32) out.push(0xa0 | encoded.length);
      else if (encoded.length < 0x100) out.push(0xd9, encoded.length);
      else out.push(0xda, encoded.length >> 8, encoded.length & 0xff);
      out.push(...encoded);
      return;
    }
    if (marker >= 0xc4 && marker <= 0xc6) {
      const width = (1 << (marker - 0xc4)) as 1 | 2 | 4;
      return copy(1 + width + size(width, at + 1));
    }
    if (marker >= 0xd4 && marker <= 0xd8)
      return copy(2 + (1 << (marker - 0xd4)));
    if (marker >= 0xc7 && marker <= 0xc9) {
      const width = (1 << (marker - 0xc7)) as 1 | 2 | 4;
      return copy(2 + width + size(width, at + 1));
    }
    const fixArray = marker >= 0x90 && marker <= 0x9f;
    const fixMap = marker >= 0x80 && marker <= 0x8f;
    if (fixArray || fixMap || (marker >= 0xdc && marker <= 0xdf)) {
      const width =
        fixArray || fixMap ? 0 : marker === 0xdc || marker === 0xde ? 2 : 4;
      const count =
        fixArray || fixMap ? marker & 0x0f : size(width as 2 | 4, at + 1);
      const items =
        fixMap || marker === 0xde || marker === 0xdf ? count * 2 : count;
      copy(1 + width);
      for (let i = 0; i < items; i++) value();
      return;
    }
    throw new Error(`Unsupported MessagePack marker 0x${marker.toString(16)}`);
  }
  value();
  if (at !== bytes.length) throw new Error('Trailing MessagePack bytes');
  return Uint8Array.from(out);
}

/** A greedy LZ4 block compressor (raw block, no frame), for building test frames. */
export function compressLz4Block(input: Uint8Array): Uint8Array {
  const out: number[] = [];
  const table = new Map<number, number>();
  const key = (i: number) =>
    input[i] |
    (input[i + 1] << 8) |
    (input[i + 2] << 16) |
    (input[i + 3] << 24);
  const length = (n: number) => {
    while (n >= 255) {
      out.push(255);
      n -= 255;
    }
    out.push(n);
  };
  // The format ends with at least 5 literals, and no match starts in the last 12 bytes.
  const matchLimit = input.length - 12;
  let anchor = 0;
  let i = 0;
  while (i < matchLimit) {
    const k = key(i);
    const candidate = table.get(k);
    table.set(k, i);
    if (
      candidate === undefined ||
      i - candidate > 0xffff ||
      key(candidate) !== k
    ) {
      i++;
      continue;
    }
    let match = 4;
    while (
      i + match < input.length - 5 &&
      input[candidate + match] === input[i + match]
    )
      match++;
    const literals = i - anchor;
    out.push((Math.min(literals, 15) << 4) | Math.min(match - 4, 15));
    if (literals >= 15) length(literals - 15);
    for (let j = anchor; j < i; j++) out.push(input[j]);
    const offset = i - candidate;
    out.push(offset & 0xff, offset >> 8);
    if (match - 4 >= 15) length(match - 4 - 15);
    i += match;
    anchor = i;
  }
  const literals = input.length - anchor;
  out.push(Math.min(literals, 15) << 4);
  if (literals >= 15) length(literals - 15);
  for (let j = anchor; j < input.length; j++) out.push(input[j]);
  return Uint8Array.from(out);
}

function msgpackInt(n: number): number[] {
  if (n < 0x80) return [n];
  if (n < 0x100) return [0xcc, n];
  if (n < 0x10000) return [0xcd, n >> 8, n & 0xff];
  return [0xd2, (n >>> 24) & 0xff, (n >> 16) & 0xff, (n >> 8) & 0xff, n & 0xff];
}

function bin(bytes: Uint8Array): number[] {
  const n = bytes.length;
  const head =
    n < 0x100
      ? [0xc4, n]
      : n < 0x10000
        ? [0xc5, n >> 8, n & 0xff]
        : [
            0xc6,
            (n >>> 24) & 0xff,
            (n >> 16) & 0xff,
            (n >> 8) & 0xff,
            n & 0xff
          ];
  return [...head, ...bytes];
}

/**
 * Wraps MessagePack bytes as MessagePack-CSharp's Lz4BlockArray: an array of an ext 98 header
 * with each block's decompressed length, then one bin per block. `split` lists block lengths
 * before the last.
 */
export function wrapLz4BlockArray(
  bytes: Uint8Array,
  split: number[] = []
): Uint8Array {
  const lengths: number[] = [];
  let at = 0;
  for (const n of split) {
    if (at + n >= bytes.length) break;
    lengths.push(n);
    at += n;
  }
  lengths.push(bytes.length - at);
  const header = lengths.flatMap(msgpackInt);
  const ext =
    header.length === 1 ||
    header.length === 2 ||
    header.length === 4 ||
    header.length === 8 ||
    header.length === 16
      ? [0xd4 + Math.log2(header.length), 98, ...header]
      : [0xc7, header.length, 98, ...header];
  const out: number[] = [0x90 | (lengths.length + 1), ...ext];
  at = 0;
  for (const n of lengths) {
    out.push(...bin(compressLz4Block(bytes.subarray(at, at + n))));
    at += n;
  }
  return Uint8Array.from(out);
}
