import crypto from 'node:crypto';

// An in-memory stand-in for the R2 release store with the conditional-write
// semantics `r2-store.mjs` provides: `ifNoneMatch` creates only, `ifMatch`
// replaces only the expected ETag, and an unconditional write is a bug.
// `beforePut` lets a test interleave a competing writer or a failure.
export class MemoryStore {
  objects = new Map();
  writes = [];
  generation = 0;
  beforePut;
  async get(key) {
    return this.objects.get(key) ?? null;
  }
  async head(key) {
    const object = this.objects.get(key);
    return object
      ? {
          size: object.bytes.length,
          sha256: crypto
            .createHash('sha256')
            .update(object.bytes)
            .digest('hex'),
          etag: object.etag
        }
      : null;
  }
  async put(key, bytes, { ifMatch, ifNoneMatch } = {}) {
    if (this.beforePut) await this.beforePut(key, bytes);
    const current = this.objects.get(key);
    if ((ifNoneMatch && current) || (ifMatch && current?.etag !== ifMatch))
      return false;
    if (!ifNoneMatch && !ifMatch) throw new Error('unconditional write');
    this.objects.set(key, {
      bytes: Buffer.from(bytes),
      etag: `"${++this.generation}"`
    });
    this.writes.push(key);
    return true;
  }
}
