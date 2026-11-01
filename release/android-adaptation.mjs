import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { INTEROP_ASSET_ROOT, buildInteropPackage } from './android-app.mjs';
import {
  PRE_ADAPTATION_PURPOSE,
  adaptationRecordKey,
  buildAdaptationRecord,
  exportReleaseKey,
  interopStatement,
  signWithReleaseKey,
  validateAdaptationRecord
} from './android-interop.mjs';
import {
  INTEROP_KEY,
  fetchInteropSnapshot,
  gameLibsStoreFromEnvironment,
  lockedInteropEntry
} from './game-libs.mjs';
import {
  ANDROID_RELEASE_SIGNER_SHA256,
  interopPackageKey
} from './manifest.mjs';
import { readZipEntries } from './payload-zip.mjs';
import { WORKSPACE_ROOT } from './product.mjs';
import { putImmutable } from './r2-store.mjs';

// What android-apk-watch.yml does after it captures a new official APK
// (docs/adr/0012-android-app.md "预适配 interop 包", docs/release.md "Android
// 发布顺序"). captureKind sorts the capture: `content` when the game code is
// the locked one, `code` when it changed. For `code`, the capture job packs
// the Interop Package of the new snapshot (packPreAdaptation), and a
// separate signing job, which runs nothing from the APK, signs its
// `pre-adaptation` statement with the Android release key and publishes the
// zip and the Pre-Adaptation record (publishPreAdaptation). A Pre-Adaptation
// is not a Product Release: it changes no VERSION and no platform manifest.

const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value) => Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

// The files packPreAdaptation hands to the signing job, in one directory.
export const PRE_ADAPTATION_FILES = Object.freeze({
  zip: 'interop.zip',
  entry: 'entry.json'
});

// How a captured android-online entry differs from the previous one:
// `unchanged` for the same APK, `content` when the APK changed but the game
// code (libil2cpp.so and global-metadata.dat) did not, `code` otherwise. The
// Android App judges by the game code alone, so a content-only update needs
// no new adaptation and no release. The same game code must give the same
// Interop Snapshot; a different one means the capture is not reproducible,
// and nobody should ship it unreviewed.
export function captureKind(previous, next) {
  if (!next) throw new Error(`No captured ${INTEROP_KEY} entry to compare`);
  if (!previous) return 'code';
  if (previous.apkSha256 === next.apkSha256) return 'unchanged';
  if (
    previous.il2cppSha256 !== next.il2cppSha256 ||
    previous.metadataSha256 !== next.metadataSha256
  )
    return 'code';
  if (previous.interopSha256 !== next.interopSha256)
    throw new Error(
      `The new APK has the locked game code but its Interop Snapshot hashes to ${next.interopSha256}, not the locked ${previous.interopSha256}; check the capture toolchain before recording it`
    );
  return 'content';
}

// Writes the Interop Package of the locked android-online snapshot and that
// lock entry into `out`, for publishPreAdaptation on another runner.
export async function packPreAdaptation({
  workspaceRoot = WORKSPACE_ROOT,
  out,
  storeFactory = gameLibsStoreFromEnvironment,
  log = () => {}
}) {
  const entry = lockedInteropEntry(workspaceRoot);
  const { interopPath } = await fetchInteropSnapshot({
    workspaceRoot,
    storeFactory,
    log
  });
  const zip = buildInteropPackage(interopPath);
  fs.rmSync(out, { recursive: true, force: true });
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, PRE_ADAPTATION_FILES.zip), zip);
  fs.writeFileSync(
    path.join(out, PRE_ADAPTATION_FILES.entry),
    jsonBytes(entry)
  );
  return {
    size: zip.length,
    sha256: sha256(zip),
    gameVersion: entry.gameVersion
  };
}

// An Interop Package holds top-level files under the interop asset root and
// nothing else (docs/adr/0012-android-app.md "发布产物").
function assertInteropPackage(zip) {
  let entries;
  try {
    entries = readZipEntries(zip);
  } catch (error) {
    throw new Error(`The Interop Package is not a zip: ${error.message}`);
  }
  const prefix = `${INTEROP_ASSET_ROOT}/`;
  const stray = entries
    .map(({ name }) => name)
    .filter(
      (name) =>
        !name.startsWith(prefix) ||
        !/^[^/\\]+$/.test(name.slice(prefix.length)) ||
        name.slice(prefix.length) === '..'
    );
  if (!entries.length || stray.length)
    throw new Error(
      `The Interop Package must hold only files under ${prefix}${stray.length ? `, not ${stray.join(', ')}` : ''}`
    );
}

// The facts of a record that identify one Pre-Adaptation, without the time it
// was published: a rerun signs the same statement to the same signature.
const adaptationFacts = (record) =>
  JSON.stringify({
    game: {
      gameVersion: record.game.gameVersion,
      versionCode: record.game.versionCode,
      il2cppSha256: record.game.il2cppSha256,
      metadataSha256: record.game.metadataSha256
    },
    interop: {
      url: record.interop.url,
      size: record.interop.size,
      sha256: record.interop.sha256,
      snapshotSha256: record.interop.snapshotSha256,
      signature: record.interop.signature
    },
    loader: { tag: record.loader.tag, sha256: record.loader.sha256 },
    il2cppInteropVersion: record.il2cppInteropVersion
  });

function readRecord(object) {
  try {
    return validateAdaptationRecord(JSON.parse(object.bytes.toString('utf8')));
  } catch {
    return null;
  }
}

// Creates the record at `key`. A record of the same Pre-Adaptation stays as it
// is (a rerun); a record of another one stays too unless `force` replaces it
// (a manual run that corrects a wrong record).
async function writeRecord({ store, key, record, force }) {
  const bytes = jsonBytes(record);
  const contentType = 'application/json';
  if (await store.put(key, bytes, { ifNoneMatch: true, contentType }))
    return 'created';
  const existing = await store.get(key);
  if (!existing)
    throw new Error(`${key} vanished while it was written; run again`);
  const current = readRecord(existing);
  if (current && adaptationFacts(current) === adaptationFacts(record))
    return 'present';
  if (!force)
    throw new Error(
      `${key} already records another Pre-Adaptation; run android-apk-watch.yml by hand with force to replace it`
    );
  if (!(await store.put(key, bytes, { ifMatch: existing.etag, contentType })))
    throw new Error(`Another writer replaced ${key} first; run again`);
  return 'replaced';
}

// Signs the `pre-adaptation` statement of the Interop Package in `from` (what
// packPreAdaptation wrote) with the Android release key, then stores the zip
// under its content address and the Pre-Adaptation record, in that order, so
// a record never names a zip that is not there. Nothing is written when the
// zip or the key is wrong.
export async function publishPreAdaptation({
  from,
  store,
  signing,
  baseUrl,
  force = false,
  releaseKey = exportReleaseKey,
  releaseSignerSha256 = ANDROID_RELEASE_SIGNER_SHA256,
  now = new Date(),
  log = () => {}
}) {
  const zip = fs.readFileSync(path.join(from, PRE_ADAPTATION_FILES.zip));
  const entry = JSON.parse(
    fs.readFileSync(path.join(from, PRE_ADAPTATION_FILES.entry), 'utf8')
  );
  assertInteropPackage(zip);
  const zipSha256 = sha256(zip);
  const statement = interopStatement({
    zipSha256,
    il2cppSha256: entry.il2cppSha256,
    metadataSha256: entry.metadataSha256,
    purpose: PRE_ADAPTATION_PURPOSE
  });
  const signature = signWithReleaseKey({
    statement,
    signing,
    signerSha256: releaseSignerSha256,
    releaseKey
  });
  const record = buildAdaptationRecord({
    baseUrl,
    game: entry,
    interop: {
      size: zip.length,
      sha256: zipSha256,
      snapshotSha256: entry.interopSha256,
      signature
    },
    loader: entry.loader,
    il2cppInteropVersion: entry.il2cppInteropVersion,
    publishedAt: now
  });
  const recordKey = adaptationRecordKey({
    il2cppSha256: record.game.il2cppSha256,
    metadataSha256: record.game.metadataSha256,
    loaderTag: record.loader.tag
  });

  // A release of the same Interop Snapshot may have stored the zip already.
  const zipKey = interopPackageKey(zipSha256);
  const stored = await store.head(zipKey);
  if (stored?.size === zip.length && stored.sha256 === zipSha256)
    log(`Already stored: ${zipKey}`);
  else {
    await putImmutable(store, zipKey, zip, 'application/zip');
    log(`Uploaded ${zipKey} (${zip.length} bytes)`);
  }
  const outcome = await writeRecord({ store, key: recordKey, record, force });
  log(
    {
      created: `Recorded ${recordKey}`,
      present: `Already recorded: ${recordKey}`,
      replaced: `Replaced ${recordKey}`
    }[outcome]
  );
  return { zipKey, recordKey, record, outcome };
}

const usage = `Usage:
  node release/android-adaptation.mjs kind --previous <game-libs.lock.json>
      Print unchanged, content or code: how the locked android-online entry
      differs from the one in the previous lock file
  node release/android-adaptation.mjs pack --out <dir>
      Write the Interop Package of the locked android-online snapshot and the
      lock entry into <dir>, for node release.mjs pre-adapt --from <dir>`;

export async function cliMain(argv, { workspaceRoot = WORKSPACE_ROOT } = {}) {
  const [command, ...rest] = argv;
  const { values } = parseArgs({
    args: rest,
    strict: true,
    options: {
      previous: { type: 'string' },
      out: { type: 'string' }
    }
  });
  if (command === 'kind' && values.previous && !values.out) {
    const previous = JSON.parse(fs.readFileSync(values.previous, 'utf8'))
      .entries?.[INTEROP_KEY];
    return captureKind(previous, lockedInteropEntry(workspaceRoot));
  }
  if (command === 'pack' && values.out && !values.previous) {
    const packed = await packPreAdaptation({
      workspaceRoot,
      out: path.resolve(values.out),
      log: (line) => console.error(line)
    });
    return `Packed the Interop Package of ${packed.gameVersion}: ${packed.size} bytes, sha256 ${packed.sha256}`;
  }
  throw new Error(usage);
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  cliMain(process.argv.slice(2)).then(
    (line) => console.log(line),
    (error) => {
      console.error(error.message);
      process.exitCode = 1;
    }
  );
