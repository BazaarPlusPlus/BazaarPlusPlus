import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { buildZipBuffer } from './payload-zip.ts';
import { gameLibsStoreFromEnvironment } from './game-assembly-snapshot.ts';
import {
  fetchInteropSnapshot,
  interopDirectoryRecords,
  lockedInteropEntry
} from './interop-snapshot.ts';
import { ANDROID_SOURCES } from './snapshot-lock.ts';
import type { LoaderPin } from './snapshot-lock.ts';
import type { AndroidCarriedRelease } from './manifest.ts';

// LemonLoader reads assets/LemonLoader/ through the game's AssetManager after the App adds each zip.

const WORKSPACE_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '..'
);
const sha256 = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');

type RuntimeLoader = Pick<LoaderPin, 'tag' | 'sha256'>;

export interface RuntimeFacts {
  schemaVersion: number;
  loader: RuntimeLoader;
  updaterSha256: string;
  runtimeZipSha256: string;
  nonManagedPayloadDigest: string | null;
}

export interface BuildRuntimeZipOptions {
  loaderPath: string;
  updaterDll: string;
}

export interface StageAndroidAppRuntimeOptions {
  loaderPath: string;
  loader: RuntimeLoader;
  updaterDll: string;
  nativeLibs: Record<string, string>;
  nonManagedPayloadDigest?: string | null;
  outDir: string;
}

export interface CarriedZip {
  sha256: string;
  file: string;
}

export interface StageCarriedReleaseOptions {
  outDir: string;
  release: AndroidCarriedRelease;
  zips: CarriedZip[];
}

interface BuildUpdaterOptions {
  workspaceRoot: string;
  loaderPath: string;
}

export const RUNTIME_ZIP = 'lemon-runtime.zip';
export const RUNTIME_FACTS = 'bpp-runtime.json';
export const RUNTIME_SCHEMA_VERSION = 1;
export const CARRIED_RELEASE_DIR = 'bpp-release';
export const CARRIED_RELEASE_FILE = 'release.json';
// LemonLoader's asset root inside the game's AssetManager.
const ASSET_ROOT = 'assets/LemonLoader';
export const INTEROP_ASSET_ROOT = `${ASSET_ROOT}/runtime/interop`;
export const UPDATER_ASSET = `${ASSET_ROOT}/deployment/Plugins/BazaarPlusPlus.Updater.dll`;
const ABI = 'arm64-v8a';
const LOADER_BOOTSTRAP = `lib/${ABI}/libmain.so`;

function filesUnder(root: string): string[] {
  const files: string[] = [];
  const walk = (directory: string): void => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const absolute = path.join(directory, entry.name);
      if (entry.isDirectory()) walk(absolute);
      else if (entry.isFile()) files.push(absolute);
      else throw new Error(`${absolute} is neither a file nor a directory`);
    }
  };
  walk(root);
  return files;
}

const zipEntry = (name: string, data: Buffer) => ({ name, data, mode: 0o644 });

export function buildInteropPackage(interopPath: string): Buffer {
  return buildZipBuffer(
    interopDirectoryRecords(interopPath).map(({ path: record }) => {
      const name = path.posix.basename(record);
      return zipEntry(
        `${INTEROP_ASSET_ROOT}/${name}`,
        fs.readFileSync(path.join(interopPath, name))
      );
    })
  );
}

export function buildRuntimeZip({
  loaderPath,
  updaterDll
}: BuildRuntimeZipOptions): Buffer {
  const root = path.join(loaderPath, ASSET_ROOT);
  if (!fs.existsSync(path.join(root, 'payload.json')))
    throw new Error(`${loaderPath} is not an unpacked Loader Release`);
  const entries = filesUnder(root).map((absolute) =>
    zipEntry(
      `${ASSET_ROOT}/${path.relative(root, absolute).split(path.sep).join('/')}`,
      fs.readFileSync(absolute)
    )
  );
  entries.push(zipEntry(UPDATER_ASSET, fs.readFileSync(updaterDll)));
  return buildZipBuffer(entries);
}

// A development build has no nonManagedPayloadDigest.
export function stageAndroidAppRuntime({
  loaderPath,
  loader,
  updaterDll,
  nativeLibs,
  nonManagedPayloadDigest = null,
  outDir
}: StageAndroidAppRuntimeOptions): RuntimeFacts {
  fs.rmSync(outDir, { recursive: true, force: true });
  const assets = path.join(outDir, 'assets');
  const jniLibs = path.join(outDir, 'jniLibs', ABI);
  fs.mkdirSync(assets, { recursive: true });
  fs.mkdirSync(jniLibs, { recursive: true });
  const runtimeZip = buildRuntimeZip({ loaderPath, updaterDll });
  fs.writeFileSync(path.join(assets, RUNTIME_ZIP), runtimeZip);
  fs.copyFileSync(
    path.join(loaderPath, LOADER_BOOTSTRAP),
    path.join(jniLibs, 'libmain.so')
  );
  for (const name of Object.keys(nativeLibs).sort())
    fs.copyFileSync(nativeLibs[name], path.join(jniLibs, name));
  const facts: RuntimeFacts = {
    schemaVersion: RUNTIME_SCHEMA_VERSION,
    loader: { tag: loader.tag, sha256: loader.sha256 },
    updaterSha256: sha256(fs.readFileSync(updaterDll)),
    runtimeZipSha256: sha256(runtimeZip),
    nonManagedPayloadDigest
  };
  fs.writeFileSync(
    path.join(assets, RUNTIME_FACTS),
    `${JSON.stringify(facts, null, 2)}\n`
  );
  return facts;
}

export function stageCarriedRelease({
  outDir,
  release,
  zips
}: StageCarriedReleaseOptions): void {
  const dir = path.join(outDir, 'assets', CARRIED_RELEASE_DIR);
  fs.mkdirSync(dir, { recursive: true });
  for (const { sha256: digest, file } of zips)
    fs.copyFileSync(file, path.join(dir, `${digest}.zip`));
  fs.writeFileSync(
    path.join(dir, CARRIED_RELEASE_FILE),
    `${JSON.stringify(release, null, 2)}\n`
  );
}

function buildUpdater({
  workspaceRoot,
  loaderPath
}: BuildUpdaterOptions): string {
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  const project = 'src/BazaarPlusPlus.Updater/BazaarPlusPlus.Updater.csproj';
  execFileSync(
    'dotnet',
    [
      'build',
      project,
      '-c',
      'Release',
      '-f',
      'net10.0',
      '--nologo',
      `-p:LemonLoaderPath=${loaderPath}${path.sep}`
    ],
    { cwd: modRoot, stdio: ['ignore', process.stderr, 'inherit'] }
  );
  return path.join(
    modRoot,
    'src/BazaarPlusPlus.Updater/bin/Release/net10.0/BazaarPlusPlus.Updater.dll'
  );
}

async function main(argv: string[]): Promise<void> {
  const [command, ...rest] = argv;
  const { values } = parseArgs({
    args: rest,
    options: { out: { type: 'string' }, source: { type: 'string' } }
  });
  // The App carries one runtime for both sources (ADR 0019), so stage-runtime
  // reads the publisher entry.
  const source = ANDROID_SOURCES.find(
    (name) => name === (values.source ?? 'publisher')
  );
  if (
    (command !== 'stage-runtime' && command !== 'interop-package') ||
    source === undefined
  )
    throw new Error(
      'Usage: bun release/android-app.ts stage-runtime|interop-package [--source publisher|play] [--out <path>]'
    );
  const workspaceRoot = WORKSPACE_ROOT;
  const entry = lockedInteropEntry(workspaceRoot, source);
  const { interopPath, loaderPath } = await fetchInteropSnapshot({
    workspaceRoot,
    source,
    storeFactory: gameLibsStoreFromEnvironment,
    log: (line: string) => console.error(line)
  });
  const appRoot = path.join(workspaceRoot, 'bazaarplusplus-android');
  if (command === 'stage-runtime') {
    const { resolveNativeLib } = await import('./android-release.ts');
    const outDir = path.resolve(
      values.out ?? path.join(appRoot, 'build/runtime')
    );
    const nativeDir = path.join(path.dirname(outDir), 'native-libs');
    const nativeLibs: Record<string, string> = {};
    for (const [name, pin] of Object.entries(entry.nativeLibs ?? {}))
      nativeLibs[name] = await resolveNativeLib({ name, pin, into: nativeDir });
    const facts = stageAndroidAppRuntime({
      loaderPath,
      loader: entry.loader,
      updaterDll: buildUpdater({ workspaceRoot, loaderPath }),
      nativeLibs,
      outDir
    });
    fs.rmSync(nativeDir, { recursive: true, force: true });
    console.log(`Staged the Android App Runtime in ${outDir}`);
    console.log(JSON.stringify(facts, null, 2));
  } else {
    const out = path.resolve(
      values.out ?? path.join(appRoot, `build/interop-${source}.zip`)
    );
    const zip = buildInteropPackage(interopPath);
    fs.mkdirSync(path.dirname(out), { recursive: true });
    fs.writeFileSync(out, zip);
    console.log(`${out} ${zip.length} bytes sha256 ${sha256(zip)}`);
  }
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  main(process.argv.slice(2)).catch((error: Error) => {
    console.error(error.message);
    process.exit(1);
  });
