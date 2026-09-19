import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { readProductVersion } from './product.mjs';
import {
  platformInventory,
  payloadEntry,
  synchronizePayloadProjection
} from './payload-inventory.mjs';
import {
  ensureNativeRecorderInput,
  verifyNativeRecorderInput,
  NATIVE_RECORDER_LOCK_PATH
} from '../bazaarplusplus-installer/scripts/release/native-recorder-input.mjs';
import {
  preparePayloadZip,
  validatePayloadZip,
  listPayloadFiles
} from '../bazaarplusplus-installer/scripts/release/payload-zip.mjs';
import {
  artifactManifestPath,
  createArtifactManifest,
  releaseSourceIdentity
} from '../bazaarplusplus-installer/scripts/release/artifact-manifest.mjs';

function hash(bytes) {
  return crypto.createHash('sha256').update(bytes).digest('hex');
}
function json(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}
function writeJson(file, value) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

export function payloadPaths(rootDir, platform) {
  if (!['macos', 'windows'].includes(platform))
    throw new Error(`Unsupported payload platform: ${platform}`);
  return {
    source: path.join(rootDir, 'src-tauri/resources/SourceForBuild', platform),
    archive: path.join(rootDir, 'src-tauri/resources/BepInExSource', platform),
    buildRecord: path.join(
      rootDir,
      'src-tauri/resources/BepInExSource',
      platform,
      'payload-build.json'
    ),
    journal: path.join(rootDir, 'src-tauri/target/payload-promotion.json')
  };
}

export function computePayloadInputs({ workspaceRoot, managedPath }) {
  const records = [];
  const scan = (directory, prefix) => {
    for (const entry of fs
      .readdirSync(directory, { withFileTypes: true })
      .sort((a, b) => a.name.localeCompare(b.name))) {
      if (
        ['bin', 'obj', 'node_modules', '.git', '.DS_Store'].includes(entry.name)
      )
        continue;
      const file = path.join(directory, entry.name);
      const relative = `${prefix}/${entry.name}`;
      if (/^mod\/native\/[^/]+\/build(?:\/|$)/.test(relative)) continue;
      if (
        relative === 'installer/scripts/release/native-recorder-input.lock.json'
      )
        continue;
      if (entry.isSymbolicLink())
        throw new Error(`Symlink in release inputs: ${relative}`);
      if (entry.isDirectory()) scan(file, relative);
      else if (entry.isFile())
        records.push({ path: relative, sha256: hash(fs.readFileSync(file)) });
    }
  };
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  for (const directory of ['src', 'build', 'tests', 'native', '.config']) {
    if (fs.existsSync(path.join(modRoot, directory)))
      scan(path.join(modRoot, directory), `mod/${directory}`);
  }
  for (const entry of fs.readdirSync(modRoot, { withFileTypes: true })) {
    if (entry.isFile() && /\.(props|targets|json|sh)$/.test(entry.name))
      records.push({
        path: `mod/${entry.name}`,
        sha256: hash(fs.readFileSync(path.join(modRoot, entry.name)))
      });
  }
  scan(path.join(workspaceRoot, 'release'), 'release');
  const installerRoot = path.join(workspaceRoot, 'bazaarplusplus-installer');
  if (fs.existsSync(path.join(installerRoot, 'scripts/release')))
    scan(
      path.join(installerRoot, 'scripts/release'),
      'installer/scripts/release'
    );
  for (const name of [
    'release.mjs',
    'bazaarplusplus-installer/src-tauri/history-database-compatibility.json'
  ]) {
    const file = path.join(workspaceRoot, name);
    if (fs.existsSync(file))
      records.push({ path: name, sha256: hash(fs.readFileSync(file)) });
  }
  records.push({
    path: 'VERSION',
    sha256: hash(fs.readFileSync(path.join(workspaceRoot, 'VERSION')))
  });
  // Game references are inputs, not just a path or a Steam branch name.
  for (const entry of fs.readdirSync(managedPath, { withFileTypes: true })) {
    if (entry.isFile() && entry.name.endsWith('.dll'))
      records.push({
        path: `managed/${entry.name}`,
        sha256: hash(fs.readFileSync(path.join(managedPath, entry.name)))
      });
  }
  if (!records.some((record) => record.path === 'managed/Assembly-CSharp.dll'))
    throw new Error('Release Managed path has no Assembly-CSharp.dll');
  records.sort((a, b) => a.path.localeCompare(b.path));
  return { digest: hash(JSON.stringify(records)), files: records };
}

function assertNoPendingPromotion(rootDir) {
  if (fs.existsSync(payloadPaths(rootDir, 'macos').journal))
    throw new Error(
      'Interrupted Payload promotion. Run node release.mjs prepare to recover before packaging.'
    );
}

function payloadOutput(rootDir, platform) {
  return listPayloadFiles(payloadPaths(rootDir, platform).source, {
    platform
  }).map(({ path: name, absolutePath, mode }) => ({
    path: name,
    mode,
    sha256: hash(fs.readFileSync(absolutePath))
  }));
}

function verifyPayloadSource({
  workspaceRoot,
  rootDir,
  platform,
  verifyNative = verifyNativeRecorderInput
}) {
  assertNoPendingPromotion(rootDir);
  const record = json(payloadPaths(rootDir, platform).buildRecord);
  if (
    record.schemaVersion !== 2 ||
    record.platform !== platform ||
    record.productVersion !== readProductVersion(workspaceRoot)
  )
    throw new Error(
      'Payload build identity is stale. Run node release.mjs prepare.'
    );
  const current = computePayloadInputs({
    workspaceRoot,
    managedPath: record.managedPath
  });
  if (current.digest !== record.inputDigest)
    throw new Error(
      'Payload build inputs changed. Run node release.mjs prepare.'
    );
  if (
    JSON.stringify(payloadOutput(rootDir, platform)) !==
    JSON.stringify(record.output)
  )
    throw new Error(
      'Payload output changed since preparation. Run node release.mjs prepare.'
    );
  verifyNative({ rootDir, platforms: [platform] });
  return record;
}

export function verifyPayloadBuild({
  workspaceRoot,
  rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer'),
  platform,
  verifyNative
}) {
  const record = verifyPayloadSource({
    workspaceRoot,
    rootDir,
    platform,
    verifyNative
  });
  validatePayloadZip({
    rootDir,
    platform,
    productVersion: record.productVersion
  });
  return record;
}

export function assertBuildOwner(
  rootDir,
  token = process.env.BPP_RELEASE_LOCK_TOKEN
) {
  const lock = json(path.join(rootDir, 'src-tauri/target/payload.lock'));
  if (
    !token ||
    lock.token !== token ||
    lock.kind !== 'build' ||
    lock.host !== os.hostname()
  )
    throw new Error('Installer packaging requires the product build lock');
  process.kill(lock.pid, 0);
}

function withPayloadLock(rootDir, kind, operation) {
  const lockPath = path.join(rootDir, 'src-tauri/target/payload.lock');
  const claimPath = `${lockPath}.claim`;
  fs.mkdirSync(path.dirname(lockPath), { recursive: true });
  // Serialize acquisition too: two reclaimers must not unlink each other's
  // newly created lock after both observed the same dead owner.
  try {
    fs.mkdirSync(claimPath);
  } catch (error) {
    if (error.code === 'EEXIST')
      throw new Error(
        `Payload lock acquisition is active or interrupted; inspect ${claimPath}`
      );
    throw error;
  }
  const token = crypto.randomUUID();
  try {
    if (fs.existsSync(lockPath)) {
      const lock = json(lockPath);
      if (lock.host !== os.hostname())
        throw new Error('Payload build is locked by another host');
      try {
        process.kill(lock.pid, 0);
        throw new Error(`Payload build is already running (pid ${lock.pid})`);
      } catch (error) {
        if (error.code !== 'ESRCH') throw error;
      }
      fs.unlinkSync(lockPath);
    }
    const fd = fs.openSync(lockPath, 'wx');
    fs.writeFileSync(
      fd,
      JSON.stringify({ pid: process.pid, host: os.hostname(), kind, token })
    );
    fs.closeSync(fd);
  } finally {
    fs.rmdirSync(claimPath);
  }
  try {
    return operation(token);
  } finally {
    fs.unlinkSync(lockPath);
  }
}

function recoverPromotion(rootDir) {
  const journalPath = payloadPaths(rootDir, 'macos').journal;
  if (!fs.existsSync(journalPath)) return;
  const journal = json(journalPath);
  const target = path.join(rootDir, 'src-tauri/target');
  if (
    journal.schemaVersion !== 1 ||
    path.dirname(journal.stageRoot) !== target ||
    !path.basename(journal.stageRoot).startsWith('payload-stage-')
  )
    throw new Error('Invalid Payload promotion journal');
  const paths = payloadPaths(rootDir, journal.platform);
  const allowed = [
    paths.source,
    paths.archive,
    path.join(rootDir, NATIVE_RECORDER_LOCK_PATH)
  ];
  for (const [index, item] of journal.replacements.entries()) {
    if (
      item.destination !== allowed[index] ||
      item.backup !== path.join(journal.stageRoot, `backup-${index}`)
    )
      throw new Error('Invalid Payload promotion destination');
  }
  if (journal.replacements.length !== allowed.length)
    throw new Error('Incomplete Payload promotion journal');
  for (const item of [...journal.replacements].reverse()) {
    if (fs.existsSync(item.backup)) {
      fs.rmSync(item.destination, { recursive: true, force: true });
      fs.renameSync(item.backup, item.destination);
    } else if (!item.existed) {
      fs.rmSync(item.destination, { recursive: true, force: true });
    }
  }
  fs.unlinkSync(journalPath);
  fs.rmSync(journal.stageRoot, { recursive: true, force: true });
}

export function promotePayload({
  rootDir,
  stageRoot,
  stagedInstaller,
  platform,
  rename = fs.renameSync
}) {
  const target = payloadPaths(rootDir, platform);
  const staged = payloadPaths(stagedInstaller, platform);
  const replacements = [
    [staged.source, target.source],
    [staged.archive, target.archive],
    [
      path.join(stagedInstaller, NATIVE_RECORDER_LOCK_PATH),
      path.join(rootDir, NATIVE_RECORDER_LOCK_PATH)
    ]
  ].map(([source, destination], index) => ({
    source,
    destination,
    backup: path.join(stageRoot, `backup-${index}`),
    existed: fs.existsSync(destination)
  }));
  writeJson(target.journal, {
    schemaVersion: 1,
    stageRoot,
    platform,
    replacements
  });
  try {
    for (const item of replacements) {
      fs.mkdirSync(path.dirname(item.destination), { recursive: true });
      if (item.existed) rename(item.destination, item.backup);
      rename(item.source, item.destination);
    }
    // The journal is the packaging gate. A crash before its removal is recovered
    // to the prior generation, even if every rename had already completed.
    fs.unlinkSync(target.journal);
  } catch (error) {
    recoverPromotion(rootDir);
    throw error;
  }
}

function copySeedPayload(sourceDir, destinationDir, platform) {
  const entries = platformInventory(platform);
  fs.mkdirSync(destinationDir, { recursive: true });
  const walk = (directory, prefix = '') => {
    for (const file of fs.readdirSync(directory, { withFileTypes: true })) {
      const relative = prefix ? `${prefix}/${file.name}` : file.name;
      if (file.isSymbolicLink())
        throw new Error(`Symlink in Payload seed: ${relative}`);
      const entry = payloadEntry(relative, entries);
      if (entry && !['bootstrap', 'native'].includes(entry.producer)) continue;
      if (file.isDirectory()) {
        walk(path.join(directory, file.name), relative);
        continue;
      }
      if (!entry) throw new Error(`Undeclared Payload seed: ${relative}`);
      const destination = path.join(destinationDir, relative);
      fs.mkdirSync(path.dirname(destination), { recursive: true });
      fs.copyFileSync(path.join(directory, file.name), destination);
      fs.chmodSync(
        destination,
        fs.statSync(path.join(directory, file.name)).mode
      );
    }
  };
  walk(sourceDir);
}

function preparePayloadUnlocked({
  workspaceRoot,
  rootDir,
  platform,
  msbuildArgs = [],
  resolveManaged,
  ensureNative = ensureNativeRecorderInput,
  verifyNative = verifyNativeRecorderInput,
  produce,
  validate = validatePayloadZip
}) {
  const hostPlatform =
    process.platform === 'darwin'
      ? 'macos'
      : process.platform === 'win32'
        ? 'windows'
        : null;
  if (!produce && hostPlatform !== platform)
    throw new Error(`Build ${platform} Payload on its native host`);
  // Only the game reference directory is caller-selectable. Build graph,
  // compiler, output and version overrides would invalidate source provenance.
  for (const arg of msbuildArgs) {
    if (!/^(?:-p:|--property:)ManagedPath=[^;\r\n]+$/.test(arg))
      throw new Error(`Unsupported release build override: ${arg}`);
  }
  recoverPromotion(rootDir);
  synchronizePayloadProjection(workspaceRoot, { check: true });
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  const managedPath = fs.realpathSync(
    resolveManaged
      ? resolveManaged()
      : execFileSync(
          'bash',
          [
            path.join(modRoot, 'run.sh'),
            'release-managed-path',
            ...msbuildArgs
          ],
          { cwd: modRoot, encoding: 'utf8' }
        ).trim()
  );
  const productVersion = readProductVersion(workspaceRoot);
  const inputs = computePayloadInputs({ workspaceRoot, managedPath });
  const stageRoot = fs.mkdtempSync(
    path.join(rootDir, 'src-tauri/target/payload-stage-')
  );
  const stagedInstaller = path.join(stageRoot, 'installer');
  try {
    const staged = payloadPaths(stagedInstaller, platform);
    copySeedPayload(
      payloadPaths(rootDir, platform).source,
      staged.source,
      platform
    );
    for (const relative of [
      'src-tauri/history-database-compatibility.json',
      NATIVE_RECORDER_LOCK_PATH
    ]) {
      const target = path.join(stagedInstaller, relative);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.copyFileSync(path.join(rootDir, relative), target);
    }
    ensureNative({ rootDir: stagedInstaller, sourceRoot: modRoot, platform });
    const resources = path.join(stagedInstaller, 'src-tauri/resources');
    const remoteDataDirectory = path.join(stageRoot, 'seeds');
    const buildArgs = [
      `-p:ManagedPath=${managedPath}`,
      `-p:BPPInstallerSourcePath=${resources}`,
      `-p:BppPayloadStagingRoot=${resources}`,
      `-p:RemoteEmbeddedDataDirectory=${remoteDataDirectory}`
    ];
    if (produce)
      produce({
        rootDir: stagedInstaller,
        platform,
        resources,
        remoteDataDirectory,
        managedPath,
        buildArgs
      });
    else
      execFileSync(
        'bash',
        [path.join(modRoot, 'run.sh'), 'produce-payload', ...buildArgs],
        {
          cwd: modRoot,
          stdio: 'inherit',
          env: {
            ...process.env,
            BPP_RELEASE_MANAGED: managedPath,
            BPP_RELEASE_ARTIFACTS: path.join(stageRoot, 'managed-artifacts')
          }
        }
      );
    if (
      computePayloadInputs({ workspaceRoot, managedPath }).digest !==
      inputs.digest
    )
      throw new Error('Release inputs changed while building the Payload');
    verifyNative({ rootDir: stagedInstaller, platforms: [platform] });
    preparePayloadZip({ rootDir: stagedInstaller, platform, productVersion });
    const archivePlaceholder = path.join(
      payloadPaths(rootDir, platform).archive,
      '.gitkeep'
    );
    if (fs.existsSync(archivePlaceholder))
      fs.copyFileSync(
        archivePlaceholder,
        path.join(staged.archive, '.gitkeep')
      );
    validate({ rootDir: stagedInstaller, platform, productVersion });
    const seeds = fs.existsSync(remoteDataDirectory)
      ? fs
          .readdirSync(remoteDataDirectory)
          .sort()
          .filter((file) => file.endsWith('.json'))
          .map((file) => ({
            path: file,
            sha256: hash(fs.readFileSync(path.join(remoteDataDirectory, file)))
          }))
      : [];
    writeJson(staged.buildRecord, {
      schemaVersion: 2,
      productVersion,
      platform,
      managedPath,
      inputDigest: inputs.digest,
      inputs: inputs.files,
      seeds,
      output: payloadOutput(stagedInstaller, platform)
    });
    promotePayload({ rootDir, stageRoot, stagedInstaller, platform });
    return json(payloadPaths(rootDir, platform).buildRecord);
  } finally {
    // Preserve backups when recovery itself failed; the journal remains a gate.
    if (!fs.existsSync(payloadPaths(rootDir, platform).journal))
      fs.rmSync(stageRoot, { recursive: true, force: true });
  }
}

export function preparePayload(options) {
  const rootDir =
    options.rootDir ??
    path.join(options.workspaceRoot, 'bazaarplusplus-installer');
  return withPayloadLock(rootDir, 'prepare', () =>
    preparePayloadUnlocked({ ...options, rootDir })
  );
}

export function buildProduct(options) {
  const rootDir =
    options.rootDir ??
    path.join(options.workspaceRoot, 'bazaarplusplus-installer');
  const {
    bundle,
    captureIdentity = releaseSourceIdentity,
    writeArtifact = createArtifactManifest
  } = options;
  return withPayloadLock(rootDir, 'build', (token) => {
    const startingCommit = captureIdentity(rootDir).commit;
    const manifestPath = artifactManifestPath(rootDir, options.platform);
    // An unsuccessful build must not leave a previous publishable receipt.
    fs.rmSync(manifestPath, { force: true });
    preparePayloadUnlocked({ ...options, rootDir });
    // Preparation can legitimately refresh tracked native producer outputs.
    // Capture packaging inputs after it, but never relabel a changed checkout.
    const identity = captureIdentity(rootDir);
    if (identity.commit !== startingCommit)
      throw new Error('Git identity changed during Payload preparation');
    verifyPayloadBuild({ ...options, rootDir });
    bundle({ token });
    // macOS signs a copy inside the archive. The unsigned source remains sealed.
    verifyPayloadSource({ ...options, rootDir });
    if (JSON.stringify(captureIdentity(rootDir)) !== JSON.stringify(identity))
      throw new Error(
        'Product source or Git identity changed during packaging; no artifact manifest was written'
      );
    return writeArtifact({
      rootDir,
      platform: options.platform,
      version: readProductVersion(options.workspaceRoot),
      gitState: identity
    });
  });
}
