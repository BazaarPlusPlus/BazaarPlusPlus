// Discard replay bytes, event JSON and the Run Message Stream to fit the 128 MiB isolate.
const MAX_BYTES = 64 * 1024 * 1024;
export class MessagePack {
  private chunk: Uint8Array = new Uint8Array(0);
  private offset = 0;
  private total = 0;
  private readonly reader: ReadableStreamDefaultReader<Uint8Array>;
  constructor(stream: ReadableStream<Uint8Array>) {
    this.reader = stream.getReader();
  }
  private async refill(): Promise<boolean> {
    if (this.offset < this.chunk.length) return true;
    const next = await this.reader.read();
    if (next.done) return false;
    this.chunk = next.value;
    this.offset = 0;
    this.total += next.value.length;
    if (this.total > MAX_BYTES) throw new Error('Run exceeds 64 MiB');
    return true;
  }
  private async take(count: number, keep = true): Promise<Uint8Array> {
    if (count > MAX_BYTES || count < 0)
      throw new Error('Invalid MessagePack length');
    const out = new Uint8Array(keep ? count : 0);
    let copied = 0;
    while (copied < count) {
      if (!(await this.refill())) throw new Error('Truncated MessagePack');
      const size = Math.min(count - copied, this.chunk.length - this.offset);
      if (keep)
        out.set(this.chunk.subarray(this.offset, this.offset + size), copied);
      this.offset += size;
      copied += size;
    }
    return out;
  }
  private async number(
    bytes: number,
    signed = false,
    float = false
  ): Promise<number> {
    const data = await this.take(bytes);
    const v = new DataView(data.buffer);
    const value = float
      ? bytes === 4
        ? v.getFloat32(0)
        : v.getFloat64(0)
      : bytes === 1
        ? signed
          ? v.getInt8(0)
          : v.getUint8(0)
        : bytes === 2
          ? signed
            ? v.getInt16(0)
            : v.getUint16(0)
          : bytes === 4
            ? signed
              ? v.getInt32(0)
              : v.getUint32(0)
            : Number(signed ? v.getBigInt64(0) : v.getBigUint64(0));
    if (!float && !Number.isSafeInteger(value))
      throw new Error('Unsafe MessagePack integer');
    return value;
  }
  private async one(
    keep = true,
    path: number[] = [],
    depth = 0
  ): Promise<unknown> {
    if (depth > 64) throw new Error('MessagePack nesting too deep');
    const m = await this.number(1);
    if (m < 0x80) return m;
    if (m >= 0xe0) return m - 256;
    if (m === 0xc0) return null;
    if (m === 0xc2 || m === 0xc3) return m === 0xc3;
    if ((m >= 0xcc && m <= 0xcf) || (m >= 0xd0 && m <= 0xd3))
      return this.number(2 ** ((m - 0xcc) % 4), m >= 0xd0);
    if (m === 0xca || m === 0xcb)
      return this.number(m === 0xca ? 4 : 8, false, true);
    if (
      (m >= 0xa0 && m <= 0xbf) ||
      (m >= 0xd9 && m <= 0xdb) ||
      (m >= 0xc4 && m <= 0xc6)
    ) {
      const string = (m >= 0xa0 && m <= 0xbf) || m >= 0xd9;
      const size =
        m >= 0xa0 && m <= 0xbf
          ? m & 31
          : await this.number(2 ** (m - (string ? 0xd9 : 0xc4)));
      const bytes = await this.take(size, keep);
      return keep
        ? string
          ? new TextDecoder('utf-8', { fatal: true }).decode(bytes)
          : bytes
        : null;
    }
    if ((m >= 0x90 && m <= 0x9f) || m === 0xdc || m === 0xdd) {
      const count = m <= 0x9f ? m & 15 : await this.number(m === 0xdc ? 2 : 4);
      if (count > MAX_BYTES) throw new Error('Invalid MessagePack array');
      const values: unknown[] = [];
      for (let i = 0; i < count; i++) {
        const retain =
          keep &&
          !(path.length === 0 && [4, 6, 7, 8].includes(i)) &&
          !(path.length === 2 && path[0] === 5 && i === 4);
        const value = await this.one(retain, [...path, i], depth + 1);
        if (keep) values.push(retain ? value : null);
      }
      return keep ? values : null;
    }
    if ((m >= 0x80 && m <= 0x8f) || m === 0xde || m === 0xdf) {
      const count = m <= 0x8f ? m & 15 : await this.number(m === 0xde ? 2 : 4);
      if (count > MAX_BYTES) throw new Error('Invalid MessagePack map');
      const values: Record<string, unknown> = Object.create(null);
      for (let i = 0; i < count; i++) {
        const key = await this.one(keep, path, depth + 1);
        const value = await this.one(keep, path, depth + 1);
        if (keep) {
          if (typeof key !== 'string' && typeof key !== 'number')
            throw new Error('Invalid map key');
          values[String(key)] = value;
        }
      }
      return keep ? values : null;
    }
    throw new Error(`Unsupported MessagePack marker ${m}`);
  }
  async decode(): Promise<unknown> {
    try {
      const value = await this.one();
      if (await this.refill()) throw new Error('Trailing MessagePack bytes');
      return value;
    } finally {
      await this.reader.cancel();
    }
  }
}
