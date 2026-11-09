#!/usr/bin/env node
// Posts a published BazaarPlusPlus release to QQ groups through the QQ official Bot:
// the Markdown notes, then one file message per installer (Win, Mac, Android).
//
//   bun scripts/workspace.ts run qqbot -- bun .agents/skills/qq-announce/scripts/announce.ts \
//     --target test|release [--notes notes.md]
//     [--platforms win,mac,android] [--groups "name,name"] [--send]
//
// --target test sends to the test group in scripts/qqbot/groups.json; --target release to every release group.
// Without --send it prints the plan and sends nothing.
// Installers come from the published manifests on the CDN, so only promoted builds go out.
// Each platform carries its own published version (desktop and Android can differ),
// and the dry run prints it for the user to check.
// Credentials: QQ_BOT_APP_ID and QQ_BOT_APP_SECRET from config.toml [qqbot].
import fs from 'node:fs';
import crypto from 'node:crypto';
import { parseArgs } from 'node:util';
import type {
  AndroidPlatformManifest,
  ReleaseArtifact,
  ReleaseManifest
} from '../../../../release/manifest.ts';
import {
  createClient,
  loadGroups,
  remedy,
  type Group,
  type QQError
} from '../../../../scripts/qqbot/client.ts';

const CDN = 'https://cdn.bazaarplusplus.com/installer';
const MAX_FILE_BYTES = 200 * 1024 * 1024; // QQ group file hard limit

// The published latest.json or latest/android-aarch64.json. Each platform's
// pick reads the installer entry of the one manifest its spec names.
type PublishedManifest = ReleaseManifest | AndroidPlatformManifest;

type PlatformName = 'win' | 'mac' | 'android';

interface PlatformSpec {
  manifest: string;
  pick: (m: PublishedManifest) => ReleaseArtifact | undefined;
  name: (v: string) => string;
}

interface PlannedFile {
  platform: PlatformName;
  version: string;
  name: string;
  url: string;
  size: number;
  sha256: string | undefined;
}

// Send order. Group display names are the QQ upload file_name, not the R2 key.
const PLATFORMS: Record<PlatformName, PlatformSpec> = {
  win: {
    manifest: `${CDN}/latest.json`,
    pick: (m) => (m as ReleaseManifest).downloads?.['windows-x86_64'],
    name: (v) => `BPP_Win_${v}.exe`
  },
  mac: {
    manifest: `${CDN}/latest.json`,
    pick: (m) => (m as ReleaseManifest).downloads?.['darwin-aarch64'],
    name: (v) => `BPP_Mac_${v}.dmg`
  },
  android: {
    manifest: `${CDN}/latest/android-aarch64.json`,
    pick: (m) => m.downloads?.['android-aarch64']?.app,
    name: (v) => `BPP_Android_${v}.apk`
  }
};

const { values: args } = parseArgs({
  options: {
    target: { type: 'string' },
    notes: { type: 'string' },
    platforms: { type: 'string', default: Object.keys(PLATFORMS).join(',') },
    groups: { type: 'string' },
    send: { type: 'boolean', default: false }
  }
});

function fail(message: string): never {
  console.error(`error: ${message}`);
  process.exit(1);
}

if (!['test', 'release'].includes(args.target ?? ''))
  fail('--target must be test or release');
const wanted = args.platforms
  .split(',')
  .map((p) => p.trim())
  .filter(Boolean);
for (const p of wanted)
  if (!(p in PLATFORMS)) fail(`unknown platform ${p}; use win, mac, android`);
const selected = Object.keys(PLATFORMS).filter((p): p is PlatformName =>
  wanted.includes(p)
);

const groupsConfig = loadGroups();
let groups: Group[] =
  args.target === 'test' ? [groupsConfig.test] : groupsConfig.release;
if (args.groups) {
  const names = args.groups.split(',').map((n) => n.trim());
  for (const n of names)
    if (!groups.some((g) => g.name === n))
      fail(`no ${args.target} group named ${n} in scripts/qqbot/groups.json`);
  groups = groups.filter((g) => names.includes(g.name));
}

async function getJson(url: string): Promise<PublishedManifest> {
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) fail(`${url} returned ${res.status}`);
  return res.json() as Promise<PublishedManifest>;
}

// Plan: each selected platform's latest published installer, named with its version.
const manifests: Record<string, PublishedManifest> = {};
const files: PlannedFile[] = [];
for (const p of selected) {
  const spec = PLATFORMS[p];
  manifests[spec.manifest] ??= await getJson(spec.manifest);
  const manifest = manifests[spec.manifest];
  const entry = spec.pick(manifest);
  if (!entry?.url)
    fail(
      `${p}: the published ${manifest.version} manifest has no installer entry`
    );
  if (entry.size > MAX_FILE_BYTES)
    fail(`${p}: ${entry.url} is ${entry.size} bytes, over the QQ 200 MB limit`);
  files.push({
    platform: p,
    version: manifest.version,
    name: spec.name(manifest.version),
    url: entry.url,
    size: entry.size,
    sha256: entry.sha256
  });
}
const notes = args.notes ? fs.readFileSync(args.notes, 'utf8').trim() : null;
if (args.notes && !notes) fail(`${args.notes} is empty`);

console.log(`Plan → ${args.target}: ${groups.map((g) => g.name).join('、')}`);
console.log('Each group gets, in order:');
let n = 1;
if (notes)
  console.log(
    `  ${n++}. Markdown notes from ${args.notes} (${notes.length} chars)`
  );
else console.log('  -  no notes message (no --notes)');
for (const f of files)
  console.log(
    `  ${n++}. ${f.name}  ${f.platform} ${f.version}  ${(f.size / 1048576).toFixed(1)} MB  from ${f.url}`
  );
if (!args.send) {
  console.log('\nDry run: nothing sent. Rerun with --send to post.');
  process.exit(0);
}

const hash = (alg: string, buf: Buffer) =>
  crypto.createHash(alg).update(buf).digest('hex');
const qq = createClient();

// Download and verify each installer once; every group gets its own upload,
// because a file uploaded through one group's interface is bound to group sends.
const buffers: Record<string, Buffer> = {};
const done: string[] = []; // "group: item" lines for the resume hint
let current: { group: Group; sent: string[] } | null = null;
try {
  for (const f of files) {
    const res = await fetch(f.url);
    if (!res.ok) throw new Error(`${f.url} returned ${res.status}`);
    const buf = Buffer.from(await res.arrayBuffer());
    if (f.size && buf.length !== f.size)
      throw new Error(
        `${f.name}: downloaded ${buf.length} bytes, manifest says ${f.size}`
      );
    if (f.sha256 && hash('sha256', buf) !== f.sha256)
      throw new Error(`${f.name}: sha256 does not match the manifest`);
    buffers[f.platform] = buf;
  }
  for (const g of groups) {
    current = { group: g, sent: [] };
    if (notes) {
      await qq.sendMarkdown(g.openid, notes);
      current.sent.push('notes');
      console.log(`${g.name}: sent notes`);
    }
    for (const f of files) {
      const { file_info } = await qq.uploadFile(
        g.openid,
        buffers[f.platform],
        f.name
      );
      await qq.sendMedia(g.openid, file_info);
      current.sent.push(f.platform);
      console.log(`${g.name}: sent ${f.name}`);
    }
    done.push(g.name);
  }
} catch (caught) {
  const e = caught as QQError;
  console.error(`error: ${e.message}`);
  const hint = remedy(e.code, current?.group.name);
  if (hint) console.error(hint);
  console.error(`Fully sent: ${done.join('、') || 'no group'}.`);
  if (current) {
    const { sent } = current;
    const left = files.map((f) => f.platform).filter((p) => !sent.includes(p));
    const notesFlag =
      notes && !current.sent.includes('notes') ? ` --notes ${args.notes}` : '';
    console.error(
      `${current.group.name} got: ${current.sent.join(', ') || 'nothing'}. Resume it with --groups "${current.group.name}" --platforms ${left.join(',')}${notesFlag}`
    );
    const rest = groups
      .slice(groups.indexOf(current.group) + 1)
      .map((g) => g.name);
    if (rest.length)
      console.error(
        `Then the untouched groups with --groups "${rest.join(',')}" and the full flags.`
      );
  }
  process.exit(1);
}
console.log(`Done: ${done.join('、')}.`);
