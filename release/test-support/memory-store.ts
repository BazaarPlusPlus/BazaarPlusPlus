import crypto from 'node:crypto';
import fs from 'node:fs';
import type {
  GetFileResult,
  ObjectStore,
  PutFileOptions,
  PutOptions,
  StoredObject,
  StoredObjectHead
} from '../r2-store.ts';

// An in-memory stand-in for the R2 release store with the conditional-write
// semantics `r2-store.ts` provides: `ifNoneMatch` creates only, `ifMatch`
// replaces only the expected ETag, and an unconditional write is a bug.
// `beforePut` lets a test interleave a competing writer or a failure.
// putFile and getFile are the streamed transfers, read whole here.
export class MemoryStore implements ObjectStore {
  objects = new Map<string, StoredObject>();
  writes: string[] = [];
  generation = 0;
  beforePut: ((key: string, bytes: Buffer) => Promise<void> | void) | undefined;
  async get(key: string): Promise<StoredObject | null> {
    return this.objects.get(key) ?? null;
  }
  async head(key: string): Promise<StoredObjectHead | null> {
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
  async put(
    key: string,
    bytes: Buffer,
    { ifMatch, ifNoneMatch }: PutOptions = {}
  ): Promise<boolean> {
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
  async putFile(
    key: string,
    file: string,
    { sha256, contentType }: Partial<PutFileOptions> = {}
  ): Promise<boolean> {
    const bytes = fs.readFileSync(file);
    if (crypto.createHash('sha256').update(bytes).digest('hex') !== sha256)
      throw new Error(`streamed body does not hash to ${sha256}`);
    return this.put(key, bytes, { ifNoneMatch: true, contentType });
  }
  async getFile(key: string, file: string): Promise<GetFileResult | null> {
    const object = this.objects.get(key);
    if (!object) return null;
    fs.writeFileSync(file, object.bytes);
    return { size: object.bytes.length };
  }
}
