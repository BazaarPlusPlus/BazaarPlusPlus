import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { buildZipBuffer } from './payload-zip.mjs';
import {
  fetchInteropSnapshot,
  gameLibsStoreFromEnvironment,
  interopDirectoryRecords,
  lockedInteropEntry
} from './game-libs.mjs';

// The Android App's inputs (docs/adr/0012-android-app.md): the Android App
// Runtime it carries (the Loader Release's bootstrap and runtime, the locked
// native libraries and the Updater) and the Interop Package it downloads.
// Both zips put their entries under assets/LemonLoader/, the path LemonLoader
// reads through the game's AssetManager once the App adds the zip as a
// resource loader. Both are deterministic: same inputs, same bytes.

const WORKSPACE_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '..'
);
const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');

export const RUNTIME_ZIP = 'lemon-runtime.zip';
export const RUNTIME_FACTS = 'bpp-runtime.json';
export const RUNTIME_SCHEMA_VERSION = 1;
// LemonLoader's asset root inside the game's AssetManager.
const ASSET_ROOT = 'assets/LemonLoader';
export const INTEROP_ASSET_ROOT = `${ASSET_ROOT}/runtime/interop`;
// The Updater is the App's one packaged LemonLoader deployment.
export const UPDATER_ASSET = `${ASSET_ROOT}/deployment/Plugins/BazaarPlusPlus.Updater.dll`;
const ABI = 'arm64-v8a';
const LOADER_BOOTSTRAP = `lib/${ABI}/libmain.so`;

function filesUnder(root) {
  const files = [];
  const walk = (directory) => {
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

const zipEntry = (name, data) => ({ name, data, mode: 0o644 });

// The Interop Package: every top-level file of an Interop Snapshot directory
// under assets/LemonLoader/runtime/interop/, and nothing else.
export function buildInteropPackage(interopPath) {
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

// The Android App Runtime zip: the Loader Release's assets/LemonLoader/
// (payload.json and runtime/) and the Updater as the packaged deployment.
export function buildRuntimeZip({ loaderPath, updaterDll }) {
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

// Writes what the App's Gradle build takes as -Pbpp.runtimeDir:
// assets/lemon-runtime.zip, assets/bpp-runtime.json, and under
// jniLibs/arm64-v8a/ the loader bootstrap libmain.so and each locked native
// library. `nonManagedPayloadDigest` is null for a development build.
export function stageAndroidAppRuntime({
  loaderPath,
  loader,
  updaterDll,
  nativeLibs,
  nonManagedPayloadDigest = null,
  outDir
}) {
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
  const facts = {
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

// The Updater's Android form (net10.0, a MelonPlugin) against the locked
// Loader Release.
function buildUpdater({ workspaceRoot, loaderPath }) {
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

async function main(argv) {
  const [command, ...rest] = argv;
  const { values } = parseArgs({
    args: rest,
    options: { out: { type: 'string' } }
  });
  if (command !== 'stage-runtime' && command !== 'interop-package')
    throw new Error(
      'Usage: node release/android-app.mjs stage-runtime|interop-package [--out <path>]'
    );
  const workspaceRoot = WORKSPACE_ROOT;
  const entry = lockedInteropEntry(workspaceRoot);
  const { interopPath, loaderPath } = await fetchInteropSnapshot({
    workspaceRoot,
    storeFactory: gameLibsStoreFromEnvironment,
    log: (line) => console.error(line)
  });
  const appRoot = path.join(workspaceRoot, 'bazaarplusplus-android');
  if (command === 'stage-runtime') {
    const { resolveNativeLib } = await import('./android-release.mjs');
    const outDir = path.resolve(
      values.out ?? path.join(appRoot, 'build/runtime')
    );
    const nativeDir = path.join(path.dirname(outDir), 'native-libs');
    const nativeLibs = {};
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
      values.out ?? path.join(appRoot, 'build/interop.zip')
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
  main(process.argv.slice(2)).catch((error) => {
    console.error(error.message);
    process.exit(1);
  });
