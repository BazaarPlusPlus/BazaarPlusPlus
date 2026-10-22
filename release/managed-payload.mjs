import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { platformInventory, readInventory } from './payload-inventory.mjs';
import { buildZipBuffer } from './payload-zip.mjs';
import { readHistoryDatabaseContract } from './history-database.mjs';
import { releasePlatform } from './release-platforms.mjs';

// The Managed Payload (root CONTEXT.md, mod ADR-0014): the managed and
// history-contract files of one product version on one platform, which the
// mod's self-update installs without the installer or a new APK. prepare
// writes the Managed Payload Manifest into the staged Payload beside the mod,
// so every install carries its own; build zips the manifest, its minisign
// signature and the files it names, and signs the zip. The mod's
// BazaarPlusPlus.ManagedPayload.ManagedPayloadManifest parses the manifest;
// both sides change together.

// Producers the self-update replaces.
export const MANAGED_PRODUCERS = Object.freeze(['managed', 'history-contract']);
// Producers only the installer or the APK replaces; their bytes form the
// non-managed payload digest a release and a running copy compare.
export const NON_MANAGED_PRODUCERS = Object.freeze([
  'native',
  'bootstrap',
  'sqlite-macos',
  'sqlite-windows',
  'updater'
]);

const MANIFEST_HEADER = 'bpp-managed-payload-manifest 1';
// Inside the Managed Payload zip.
export const ZIP_MANIFEST = 'manifest.txt';
export const ZIP_SIGNATURE = 'manifest.txt.minisig';
export const ZIP_FILES = 'files';

const SHA256 = /^[a-f0-9]{64}$/;
const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
// Ordinal order, as the mod's parser requires (string.CompareOrdinal).
const ordinal = (left, right) => (left < right ? -1 : left > right ? 1 : 0);

function onlyEntry(platform, producer, inventory) {
  const entries = platformInventory(platform, inventory).filter(
    (entry) => entry.producer === producer
  );
  if (entries.length !== 1)
    throw new Error(
      `The ${platform} Payload Inventory must name exactly one ${producer} entry; found ${entries.length}`
    );
  return entries[0];
}

// Where the installed Managed Payload Manifest sits, relative to the
// platform's install root.
export const installedManifestPath = (platform, inventory = readInventory()) =>
  onlyEntry(platform, 'managed-manifest', inventory).path;

function staged(sourceDir, relative) {
  return path.join(sourceDir, ...relative.split('/'));
}

// The files under one inventory entry in a staged Payload, as install paths.
function entryFiles(sourceDir, entry) {
  const root = staged(sourceDir, entry.path);
  const stat = fs.statSync(root, { throwIfNoEntry: false });
  if (!stat) {
    if (entry.optional || entry.producer === 'runtime') return [];
    throw new Error(
      `The staged Payload lacks ${entry.path} (${entry.producer})`
    );
  }
  if (!stat.isDirectory()) return [entry.path];
  const files = [];
  const walk = (directory, prefix) => {
    for (const dirent of fs.readdirSync(directory, { withFileTypes: true })) {
      const relative = `${prefix}/${dirent.name}`;
      if (dirent.isDirectory())
        walk(path.join(directory, dirent.name), relative);
      else files.push(relative);
    }
  };
  walk(root, entry.path);
  return files;
}

const fileRecords = (sourceDir, paths) =>
  paths.sort(ordinal).map((relative) => {
    const bytes = fs.readFileSync(staged(sourceDir, relative));
    return { path: relative, size: bytes.length, sha256: sha256(bytes) };
  });

// The managed and history-contract files of a staged Payload.
export function managedPayloadFiles(
  sourceDir,
  platform,
  inventory = readInventory()
) {
  return fileRecords(
    sourceDir,
    platformInventory(platform, inventory)
      .filter((entry) => MANAGED_PRODUCERS.includes(entry.producer))
      .flatMap((entry) => entryFiles(sourceDir, entry))
  );
}

// The digest of every non-managed file a desktop Payload installs: the set a
// Managed Payload cannot change. Android's comes from its APK identity
// (manifest.mjs androidNonManagedPayloadDigest).
export function desktopNonManagedPayloadDigest(
  sourceDir,
  platform,
  inventory = readInventory()
) {
  const files = fileRecords(
    sourceDir,
    platformInventory(platform, inventory)
      .filter((entry) => NON_MANAGED_PRODUCERS.includes(entry.producer))
      .flatMap((entry) => entryFiles(sourceDir, entry))
  ).map(({ path: name, sha256: digest }) => ({ path: name, sha256: digest }));
  return sha256(JSON.stringify(files));
}

export function renderManagedPayloadManifest({
  version,
  platformKey,
  historyDatabaseUserVersion,
  nonManagedPayloadDigest,
  files
}) {
  if (
    !/^\d+\.\d+\.\d+$/.test(version ?? '') ||
    !/^[A-Za-z0-9_-]+$/.test(platformKey ?? '') ||
    !Number.isSafeInteger(historyDatabaseUserVersion) ||
    historyDatabaseUserVersion <= 0 ||
    !SHA256.test(nonManagedPayloadDigest ?? '') ||
    !files?.length
  )
    throw new Error('Invalid Managed Payload Manifest facts');
  const lines = [
    MANIFEST_HEADER,
    `version ${version}`,
    `platform ${platformKey}`,
    `history-database-user-version ${historyDatabaseUserVersion}`,
    `non-managed-payload-digest ${nonManagedPayloadDigest}`,
    ...[...files]
      .sort((left, right) => ordinal(left.path, right.path))
      .map((file) => `file ${file.sha256} ${file.size} ${file.path}`)
  ];
  return `${lines.join('\n')}\n`;
}

export function parseManagedPayloadManifest(text) {
  const lines = text.endsWith('\n') ? text.slice(0, -1).split('\n') : [];
  const field = (line, name) => {
    if (!line?.startsWith(`${name} `))
      throw new Error(`Managed Payload Manifest lacks ${name}`);
    return line.slice(name.length + 1);
  };
  if (lines[0] !== MANIFEST_HEADER)
    throw new Error('Not a Managed Payload Manifest');
  const files = lines.slice(5).map((line) => {
    const match = /^file ([a-f0-9]{64}) (\d+) (.+)$/.exec(line);
    if (!match)
      throw new Error(`Invalid Managed Payload Manifest line: ${line}`);
    return { path: match[3], size: Number(match[2]), sha256: match[1] };
  });
  return {
    version: field(lines[1], 'version'),
    platformKey: field(lines[2], 'platform'),
    historyDatabaseUserVersion: Number(
      field(lines[3], 'history-database-user-version')
    ),
    nonManagedPayloadDigest: field(lines[4], 'non-managed-payload-digest'),
    files
  };
}

// Writes the Managed Payload Manifest into a staged Payload, beside the mod.
export function writeManagedPayloadManifest({
  sourceDir,
  platform,
  version,
  nonManagedPayloadDigest,
  inventory = readInventory()
}) {
  const contract = onlyEntry(platform, 'history-contract', inventory);
  const { historyDatabaseUserVersion } = readHistoryDatabaseContract(
    staged(sourceDir, contract.path)
  );
  const text = renderManagedPayloadManifest({
    version,
    platformKey: releasePlatform(platform).key,
    historyDatabaseUserVersion,
    nonManagedPayloadDigest,
    files: managedPayloadFiles(sourceDir, platform, inventory)
  });
  const target = staged(sourceDir, installedManifestPath(platform, inventory));
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, text);
  return parseManagedPayloadManifest(text);
}

// Reads a staged Payload's manifest and checks every file it names is still
// the prepared one.
export function readStagedManagedPayload(
  sourceDir,
  platform,
  inventory = readInventory()
) {
  const relative = installedManifestPath(platform, inventory);
  const text = fs.readFileSync(staged(sourceDir, relative), 'utf8');
  const manifest = parseManagedPayloadManifest(text);
  if (manifest.platformKey !== releasePlatform(platform).key)
    throw new Error(
      `${relative} is the Managed Payload Manifest of ${manifest.platformKey}, not ${platform}`
    );
  const current = managedPayloadFiles(sourceDir, platform, inventory);
  if (JSON.stringify(current) !== JSON.stringify(manifest.files))
    throw new Error(
      `The staged ${platform} Managed Payload changed since preparation; run node release.mjs prepare --platform ${platform}`
    );
  return { manifest, text };
}

// The Managed Payload zip: the manifest and its signature at the root, the
// files under files/<install path>. `sign({ file })` returns the minisign
// signature of a file, as signWithUpdaterKey does.
export function writeManagedPayloadZip({
  sourceDir,
  platform,
  outputPath,
  sign,
  inventory = readInventory()
}) {
  const { manifest, text } = readStagedManagedPayload(
    sourceDir,
    platform,
    inventory
  );
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-managed-'));
  let signature;
  try {
    const manifestFile = path.join(scratch, ZIP_MANIFEST);
    fs.writeFileSync(manifestFile, text);
    signature = sign({ file: manifestFile }).trim();
  } finally {
    fs.rmSync(scratch, { recursive: true, force: true });
  }
  if (!signature)
    throw new Error('The Managed Payload Manifest signature is empty');
  const entry = (name, data) => ({ name, data, mode: 0o644 });
  const buffer = buildZipBuffer([
    entry(ZIP_MANIFEST, Buffer.from(text)),
    entry(ZIP_SIGNATURE, Buffer.from(`${signature}\n`)),
    ...manifest.files.map((file) =>
      entry(
        `${ZIP_FILES}/${file.path}`,
        fs.readFileSync(staged(sourceDir, file.path))
      )
    )
  ]);
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, buffer);
  return {
    path: outputPath,
    size: buffer.length,
    sha256: sha256(buffer),
    manifest
  };
}

function installerRoot(workspaceRoot) {
  return path.join(workspaceRoot, 'bazaarplusplus-installer');
}

// Signs a file with the Tauri updater key (minisign) through the installer's
// pinned @tauri-apps/cli, as the Tauri updater packages are signed; returns
// the signature text the fragment carries. Reads the key from
// BPP_SIGNING_SECRETS_DIR/tauri-updater.key, else TAURI_SIGNING_PRIVATE_KEY.
export function signWithUpdaterKey({ workspaceRoot, file, env = process.env }) {
  // The CLI's own entry script, run by this Node: a .cmd shim needs a shell on
  // Windows.
  const tauri = path.join(
    installerRoot(workspaceRoot),
    'node_modules',
    '@tauri-apps',
    'cli',
    'tauri.js'
  );
  if (!fs.existsSync(tauri))
    throw new Error(
      `Signing the Managed Payload needs the installer's Tauri CLI; run npm ci in bazaarplusplus-installer (missing ${tauri})`
    );
  const secrets = env.BPP_SIGNING_SECRETS_DIR;
  const keyFile = secrets ? path.join(secrets, 'tauri-updater.key') : '';
  const passwordFile = secrets
    ? path.join(secrets, 'tauri-updater.password')
    : '';
  const signing = { ...env };
  delete signing.TAURI_SIGNING_PRIVATE_KEY_PATH;
  if (keyFile && fs.existsSync(keyFile)) {
    delete signing.TAURI_SIGNING_PRIVATE_KEY;
    signing.TAURI_SIGNING_PRIVATE_KEY_PATH = keyFile;
  } else if (!env.TAURI_SIGNING_PRIVATE_KEY)
    throw new Error(
      'Signing the Managed Payload needs the Tauri updater key: BPP_SIGNING_SECRETS_DIR/tauri-updater.key or TAURI_SIGNING_PRIVATE_KEY'
    );
  if (passwordFile && fs.existsSync(passwordFile))
    signing.TAURI_SIGNING_PRIVATE_KEY_PASSWORD = fs
      .readFileSync(passwordFile, 'utf8')
      .replace(/\n+$/, '');
  signing.TAURI_SIGNING_PRIVATE_KEY_PASSWORD ??= '';
  fs.rmSync(`${file}.sig`, { force: true });
  try {
    execFileSync(process.execPath, [tauri, 'signer', 'sign', file], {
      env: signing,
      stdio: ['ignore', 'pipe', 'pipe']
    });
  } catch (error) {
    throw new Error(
      `tauri signer sign failed: ${String(error.stderr || error.message).trim()}`
    );
  }
  return fs.readFileSync(`${file}.sig`, 'utf8').trim();
}
