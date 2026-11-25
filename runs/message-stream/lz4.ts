/**
 * Decompresses one raw LZ4 block (no frame header) whose decompressed length is known, as
 * MessagePack-CSharp's Lz4BlockArray stores it. Every read and write is bounds-checked.
 */
export function decompressLz4Block(
  source: Uint8Array,
  outputLength: number
): Uint8Array {
  const out = new Uint8Array(outputLength);
  let s = 0;
  let d = 0;

  function extend(start: number): number {
    let total = start;
    for (;;) {
      if (s >= source.length) throw new Error('Truncated LZ4 length');
      const byte = source[s++];
      total += byte;
      if (byte !== 255) return total;
    }
  }

  while (s < source.length) {
    const token = source[s++];
    let literals = token >> 4;
    if (literals === 15) literals = extend(15);
    if (s + literals > source.length || d + literals > outputLength) {
      throw new Error('LZ4 literals overrun');
    }
    out.set(source.subarray(s, s + literals), d);
    s += literals;
    d += literals;
    // The last sequence carries literals only.
    if (s === source.length) break;
    if (s + 2 > source.length) throw new Error('Truncated LZ4 offset');
    const offset = source[s] | (source[s + 1] << 8);
    s += 2;
    if (offset === 0 || offset > d) throw new Error('Invalid LZ4 offset');
    let match = token & 15;
    if (match === 15) match = extend(15);
    match += 4;
    if (d + match > outputLength) throw new Error('LZ4 match overrun');
    if (offset >= match) {
      out.copyWithin(d, d - offset, d - offset + match);
      d += match;
    } else {
      // An overlapping match repeats the bytes it is still writing.
      for (let i = 0; i < match; i++, d++) out[d] = out[d - offset];
    }
  }
  if (d !== outputLength) throw new Error('LZ4 block length mismatch');
  return out;
}
