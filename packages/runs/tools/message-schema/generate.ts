// The Game Message Schema generator. See runs/message-stream/schema.ts.
//
//   bun runs/tools/message-schema/generate.ts [snapshot...]   merge snapshots under
//       bazaarplusplus-mod/game-libs/ into the committed schema (default: the Snapshot Lock's
//       macOS online and staging and its Android entries that are on disk); needs the .NET SDK.
//       The schema is cumulative: a build stays once merged, so older Runs keep their names.
//       Until a new game build is merged, its new keys and union cases stay raw and flagged,
//       never lost.
//   bun runs/tools/message-schema/generate.ts check           fail when the committed schema is not
//       what the generator writes for the sources it records; needs no snapshot
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import type {
  GameMessageSchema,
  SchemaSource
} from '../../message-stream/schema';
import {
  EMPTY_SCHEMA,
  mergeSource,
  normalizeSchema,
  serializeSchema,
  type Extraction
} from './merge';

const RUNS = path.resolve(import.meta.dirname, '../..');
const REPO_ROOT = path.resolve(RUNS, '../..');
const GAME_LIBS = path.resolve(REPO_ROOT, 'bazaarplusplus-mod/game-libs');
const LOCK = path.resolve(
  REPO_ROOT,
  'bazaarplusplus-mod/build/game-libs.lock.json'
);
export const SCHEMA_PATH = path.join(
  RUNS,
  'message-stream/game-message-schema.json'
);

type Manifest = {
  platform: string;
  gameVersion: string;
  versionCode?: number;
};

export function snapshotSource(name: string, manifest: Manifest): SchemaSource {
  if (manifest.platform === 'android') {
    if (typeof manifest.versionCode !== 'number')
      throw new Error(`${name}: no versionCode`);
    return {
      id: `android-${manifest.versionCode}`,
      gameVersion: manifest.gameVersion,
      platform: 'android',
      layout: 'inferred'
    };
  }
  const build = /^\d+\.\d+\.(\d+)-/.exec(manifest.gameVersion)?.[1];
  if (
    !build ||
    (manifest.platform !== 'macos' && manifest.platform !== 'windows')
  )
    throw new Error(`${name}: unexpected snapshot ${manifest.gameVersion}`);
  return {
    id: `${manifest.platform}-${build}`,
    gameVersion: manifest.gameVersion,
    platform: manifest.platform,
    layout: 'keyed'
  };
}

function readSchema(): GameMessageSchema {
  return fs.existsSync(SCHEMA_PATH)
    ? (JSON.parse(fs.readFileSync(SCHEMA_PATH, 'utf8')) as GameMessageSchema)
    : EMPTY_SCHEMA;
}

function extract(directory: string, source: SchemaSource): Extraction {
  const dll =
    source.layout === 'keyed'
      ? path.join(directory, 'Managed/BazaarGameShared.dll')
      : path.join(directory, 'interop/Il2CppBazaarGameShared.dll');
  const out = path.join(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-message-schema-')),
    'layout.json'
  );
  execFileSync(
    'dotnet',
    [
      'run',
      path.join(import.meta.dirname, 'extract.cs'),
      '--',
      dll,
      source.layout === 'keyed' ? 'attributes' : 'names',
      out
    ],
    { stdio: ['ignore', 'ignore', 'inherit'] }
  );
  return JSON.parse(fs.readFileSync(out, 'utf8')) as Extraction;
}

type Lock = {
  entries: Record<string, { gameVersion: string; versionCode?: number } | null>;
};

function readLock(): Lock {
  return JSON.parse(fs.readFileSync(LOCK, 'utf8')) as Lock;
}

/**
 * Snapshot directory names of the lock's macOS and Android entries. ptr is left out: PTR Runs
 * are never sealed (mod ADR-0028).
 */
function lockedSnapshots(lock: Lock): string[] {
  return Object.entries(lock.entries).flatMap(([key, entry]) =>
    !entry || key === 'macos-ptr'
      ? []
      : key.startsWith('macos-')
        ? [`${key}-${entry.gameVersion}`]
        : key.startsWith('android-')
          ? [`${key}-${entry.versionCode}`]
          : []
  );
}

function generate(names: string[]) {
  const wanted = names.length ? names : lockedSnapshots(readLock());
  const present = wanted.filter((name) =>
    fs.existsSync(path.join(GAME_LIBS, name, 'manifest.json'))
  );
  for (const name of wanted)
    if (!present.includes(name))
      process.stderr.write(`Skipping ${name}: not under game-libs/\n`);
  const picked = present.map((name) => {
    const directory = path.join(GAME_LIBS, name);
    const manifest = JSON.parse(
      fs.readFileSync(path.join(directory, 'manifest.json'), 'utf8')
    ) as Manifest;
    return { directory, source: snapshotSource(name, manifest) };
  });
  // Only keyed builds place members, so they merge first.
  picked.sort(
    (a, b) =>
      Number(a.source.layout === 'inferred') -
        Number(b.source.layout === 'inferred') ||
      a.source.id.localeCompare(b.source.id)
  );
  let schema = readSchema();
  for (const { directory, source } of picked) {
    process.stderr.write(`Reading ${path.basename(directory)}\n`);
    schema = mergeSource(schema, source, extract(directory, source));
  }
  fs.writeFileSync(SCHEMA_PATH, serializeSchema(schema));
  process.stderr.write(
    `Wrote ${path.relative(RUNS, SCHEMA_PATH)}: ${schema.sources.length} sources, ${Object.keys(schema.types).length} types\n`
  );
}

/**
 * Problems with the committed schema; empty when it is exactly what the generator writes for
 * the sources it records (sorted, every source id known, `sources` left out where it equals
 * the type's). The test re-reads each recorded snapshot that is on disk.
 */
export function schemaProblems(schemaText: string): string[] {
  const schema = JSON.parse(schemaText) as GameMessageSchema;
  const problems: string[] = [];
  if (serializeSchema(normalizeSchema(schema)) !== schemaText)
    problems.push('the schema is not what the generator writes');
  const known = new Set(schema.sources.map((s) => s.id));
  const used = new Set<string>();
  const add = (ids: string[] | undefined) => ids?.forEach((id) => used.add(id));
  for (const type of Object.values(schema.types)) {
    add(type.sources);
    if (type.kind === 'object') {
      for (const m of type.members) add(m.sources);
      for (const u of type.unkeyed ?? []) add(u.sources);
    } else if (type.kind === 'union') {
      for (const c of type.cases) add(c.sources);
    } else for (const v of type.values) add(v.sources);
  }
  for (const id of used)
    if (!known.has(id)) problems.push(`source ${id} is not recorded`);
  if (!schema.sources.some((s) => s.layout === 'keyed'))
    problems.push('no keyed (desktop) source');
  return problems;
}

function check() {
  const problems = schemaProblems(fs.readFileSync(SCHEMA_PATH, 'utf8'));
  if (problems.length) {
    for (const problem of problems) process.stderr.write(`${problem}\n`);
    process.stderr.write(
      "Regenerate the Game Message Schema with 'just runs::message-schema'\n"
    );
    process.exit(1);
  }
}

if (import.meta.main) {
  const [command, ...rest] = process.argv.slice(2);
  if (command === 'check') check();
  else generate(command ? [command, ...rest] : []);
}
