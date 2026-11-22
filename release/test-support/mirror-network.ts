import fs from 'node:fs';
import path from 'node:path';
import { MAINLAND_MIRROR } from '../mirror.ts';
import type { FetchImpl } from '../r2-store.ts';
import type { MemoryStore } from './memory-store.ts';

export interface MirrorPush {
  args: readonly string[];
  /** The files rsync would send, by their path on the mirror. */
  files: Record<string, Buffer>;
  key: string;
  keyMode: number;
  knownHosts: string;
}

export interface MirrorNetwork {
  /** What the mirror serves, by `<version>/<fileName>`. */
  served: Map<string, Buffer>;
  pushes: MirrorPush[];
  requests: string[];
  runRsync: (args: readonly string[]) => Promise<void>;
  fetchImpl: FetchImpl;
}

const bytesResponse = (bytes: Buffer): Response =>
  new Response(new Uint8Array(bytes), {
    status: 200,
    headers: { 'content-length': String(bytes.length) }
  });

function filesUnder(directory: string, root: string): Record<string, Buffer> {
  const files: Record<string, Buffer> = {};
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) Object.assign(files, filesUnder(file, root));
    else
      files[path.relative(root, file).split(path.sep).join('/')] =
        fs.readFileSync(file);
  }
  return files;
}

// The release origin serves the store's objects. The mirror keeps the first
// copy of every path, as rrsync -no-overwrite does, and serves it over https.
export function mirrorNetwork(
  store: MemoryStore,
  releaseBaseUrl: string
): MirrorNetwork {
  const served = new Map<string, Buffer>();
  const pushes: MirrorPush[] = [];
  const requests: string[] = [];
  const option = (ssh: string, pattern: RegExp): string => {
    const match = pattern.exec(ssh);
    if (!match) throw new Error(`ssh command lacks ${pattern}: ${ssh}`);
    return match[1];
  };
  return {
    served,
    pushes,
    requests,
    async runRsync(args) {
      const ssh = args[args.indexOf('-e') + 1];
      const key = option(ssh, /-i (\S+)/);
      const source = args.at(-2) as string;
      const files = filesUnder(source, path.dirname(source));
      pushes.push({
        args: [...args],
        files,
        key: fs.readFileSync(key, 'utf8'),
        keyMode: fs.statSync(key).mode & 0o777,
        knownHosts: fs.readFileSync(
          option(ssh, /UserKnownHostsFile=(\S+)/),
          'utf8'
        )
      });
      if (args.at(-1) !== `${MAINLAND_MIRROR.sshTarget}:`)
        throw new Error(`rsync to an unknown host: ${args.at(-1)}`);
      for (const [file, bytes] of Object.entries(files))
        if (!served.has(file)) served.set(file, bytes);
    },
    async fetchImpl(url) {
      requests.push(url);
      const mirror = `${MAINLAND_MIRROR.baseUrl}/`;
      const release = `${releaseBaseUrl.replace(/\/$/, '')}/`;
      const bytes = url.startsWith(mirror)
        ? served.get(decodeURIComponent(url.slice(mirror.length)))
        : url.startsWith(release)
          ? store.objects.get(decodeURIComponent(url.slice(release.length)))
              ?.bytes
          : undefined;
      return bytes ? bytesResponse(bytes) : new Response('', { status: 404 });
    }
  };
}
