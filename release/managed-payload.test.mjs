import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test } from 'vitest';
import {
  desktopNonManagedPayloadDigest,
  installedManifestPath,
  parseManagedPayloadManifest,
  readStagedManagedPayload,
  renderManagedPayloadManifest,
  signWithUpdaterKey,
  writeManagedPayloadManifest,
  writeManagedPayloadZip
} from './managed-payload.mjs';
import { platformInventory } from './payload-inventory.mjs';
import { readZipEntries } from './payload-zip.mjs';
import { WORKSPACE_ROOT } from './product.mjs';

// The Managed Payload the release build writes for the mod's self-update
// (mod ADR-0014). Failure modes: a manifest the mod's parser would read
// differently (the shared golden), a non-managed digest that moves with a
// managed file or misses a native one, a zip that carries a file its manifest
// does not name, and a staged Payload changed after preparation.

const roots = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

const sha = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');

function write(file, bytes) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
}

// A staged macOS Payload source: every required inventory path with
// recognizable bytes, the history contract copied from the mod.
function stagedSource() {
  const sourceDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-managed-'));
  roots.push(sourceDir);
  for (const entry of platformInventory('macos')) {
    if (['runtime', 'retired', 'managed-manifest'].includes(entry.producer))
      continue;
    if (entry.optional) continue;
    const files =
      entry.kind === 'directory'
        ? (entry.requiredEntries ?? []).map((child) => `${entry.path}/${child}`)
        : [entry.path];
    for (const file of files)
      write(
        path.join(sourceDir, file),
        entry.producer === 'history-contract'
          ? fs.readFileSync(
              path.join(
                WORKSPACE_ROOT,
                'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json'
              )
            )
          : `bytes of ${file}`
      );
  }
  return sourceDir;
}

test('the shared Managed Payload Manifest golden is what the writer renders', () => {
  const produced = renderManagedPayloadManifest({
    version: '6.1.0',
    platformKey: 'darwin-aarch64',
    historyDatabaseUserVersion: 1,
    nonManagedPayloadDigest: 'e'.repeat(64),
    files: [
      {
        path: 'BepInEx/plugins/BazaarPlusPlus.dll',
        size: 3,
        sha256: sha('dll')
      },
      {
        path: 'BepInEx/plugins/BazaarPlusPlus.Core.dll',
        size: 4,
        sha256: sha('core')
      }
    ]
  });
  const golden = path.join(
    import.meta.dirname,
    'fixtures/managed-payload/manifest.txt'
  );
  if (process.env.BPP_UPDATE_GOLDENS === '1') write(golden, produced);
  expect(produced).toBe(fs.readFileSync(golden, 'utf8'));
  // Ordinal order: 'BazaarPlusPlus.Core.dll' sorts before 'BazaarPlusPlus.dll'.
  expect(
    parseManagedPayloadManifest(produced).files.map((f) => f.path)
  ).toEqual([
    'BepInEx/plugins/BazaarPlusPlus.Core.dll',
    'BepInEx/plugins/BazaarPlusPlus.dll'
  ]);
});

test('prepare writes the manifest of exactly the managed files beside the mod', () => {
  const sourceDir = stagedSource();
  const digest = desktopNonManagedPayloadDigest(sourceDir, 'macos');
  const manifest = writeManagedPayloadManifest({
    sourceDir,
    platform: 'macos',
    version: '6.1.0',
    nonManagedPayloadDigest: digest
  });
  const managed = platformInventory('macos')
    .filter(
      (entry) =>
        ['managed', 'history-contract'].includes(entry.producer) &&
        !entry.optional
    )
    .map((entry) => entry.path)
    .sort();
  expect(manifest.files.map((file) => file.path)).toEqual(managed);
  expect(manifest).toMatchObject({
    version: '6.1.0',
    platformKey: 'darwin-aarch64',
    historyDatabaseUserVersion: 1,
    nonManagedPayloadDigest: digest
  });
  expect(installedManifestPath('macos')).toBe(
    'BepInEx/plugins/BazaarPlusPlus.managed-manifest.txt'
  );
  expect(
    fs.existsSync(path.join(sourceDir, installedManifestPath('macos')))
  ).toBe(true);
});

test('the non-managed digest follows the native, bootstrap and Updater files, never the managed ones', () => {
  const sourceDir = stagedSource();
  const before = desktopNonManagedPayloadDigest(sourceDir, 'macos');
  write(path.join(sourceDir, 'BepInEx/plugins/BazaarPlusPlus.dll'), 'new mod');
  expect(desktopNonManagedPayloadDigest(sourceDir, 'macos')).toBe(before);
  for (const file of [
    'BepInEx/patchers/BazaarPlusPlus.Updater.dll',
    'BepInEx/plugins/libe_sqlite3.dylib',
    'BepInEx/core/BepInEx.dll',
    'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox'
  ]) {
    const original = fs.readFileSync(path.join(sourceDir, file));
    write(path.join(sourceDir, file), 'changed');
    expect(desktopNonManagedPayloadDigest(sourceDir, 'macos'), file).not.toBe(
      before
    );
    write(path.join(sourceDir, file), original);
  }
  fs.rmSync(
    path.join(sourceDir, 'BepInEx/patchers/BazaarPlusPlus.Updater.dll')
  );
  expect(() => desktopNonManagedPayloadDigest(sourceDir, 'macos')).toThrow(
    /lacks BepInEx\/patchers\/BazaarPlusPlus.Updater.dll/
  );
});

test('the Managed Payload zip carries the signed manifest and only the files it names', () => {
  const sourceDir = stagedSource();
  const manifest = writeManagedPayloadManifest({
    sourceDir,
    platform: 'macos',
    version: '6.1.0',
    nonManagedPayloadDigest: desktopNonManagedPayloadDigest(sourceDir, 'macos')
  });
  const signed = [];
  const outputPath = path.join(
    sourceDir,
    '..',
    `${path.basename(sourceDir)}.zip`
  );
  roots.push(outputPath);
  const zip = writeManagedPayloadZip({
    sourceDir,
    platform: 'macos',
    outputPath,
    sign: ({ file }) => {
      signed.push(fs.readFileSync(file, 'utf8'));
      return 'manifest-signature\n';
    }
  });
  const entries = readZipEntries(fs.readFileSync(outputPath));
  const names = entries.map((entry) => entry.name).sort();
  expect(names).toEqual(
    [
      'manifest.txt',
      'manifest.txt.minisig',
      ...manifest.files.map((file) => `files/${file.path}`)
    ].sort()
  );
  expect(signed).toEqual([
    fs.readFileSync(
      path.join(sourceDir, installedManifestPath('macos')),
      'utf8'
    )
  ]);
  expect(zip.sha256).toBe(sha(fs.readFileSync(outputPath)));
  expect(names).not.toContain(
    'files/BepInEx/patchers/BazaarPlusPlus.Updater.dll'
  );

  write(path.join(sourceDir, 'BepInEx/plugins/BazaarPlusPlus.dll'), 'swapped');
  expect(() => readStagedManagedPayload(sourceDir, 'macos')).toThrow(
    /changed since preparation/
  );
});

test('signing without the updater key names where the key comes from', () => {
  const file = path.join(stagedSource(), 'manifest.txt');
  write(file, 'manifest');
  expect(() =>
    signWithUpdaterKey({ workspaceRoot: WORKSPACE_ROOT, file, env: {} })
  ).toThrow(/tauri-updater.key or TAURI_SIGNING_PRIVATE_KEY/);
});
