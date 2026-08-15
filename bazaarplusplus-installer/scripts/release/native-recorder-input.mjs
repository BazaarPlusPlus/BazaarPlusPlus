import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';

export const NATIVE_RECORDER_LOCK_PATH =
  'scripts/release/native-recorder-input.lock.json';
export const NATIVE_ARTIFACT_CATALOG_PATH = 'native/artifacts.json';

const expectedRepository = 'BazaarPlusPlus/bazaarplusplus-mod';
const inputDigestDomain = Buffer.from('bpp-native-input-v1\0');

export const NATIVE_RECORDER_ARTIFACTS = Object.freeze({
  macos: Object.freeze([
    Object.freeze({
      id: 'mac-audio',
      kind: 'file',
      destinationPath:
        'src-tauri/resources/SourceForBuild/macos/BepInEx/plugins/libBppMacAudio.dylib'
    }),
    Object.freeze({
      id: 'mac-replay',
      kind: 'directory',
      destinationPath:
        'src-tauri/resources/SourceForBuild/macos/TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle'
    })
  ]),
  windows: Object.freeze([
    Object.freeze({
      id: 'windows-replay',
      kind: 'file',
      destinationPath:
        'src-tauri/resources/SourceForBuild/windows/TheBazaar_Data/Plugins/x86_64/GfxPluginBppReplayMediaFoundation.dll'
    })
  ])
});

function assertPlatform(platform) {
  if (!Object.hasOwn(NATIVE_RECORDER_ARTIFACTS, platform)) {
    throw new Error(`Unsupported native recorder platform: ${platform}`);
  }
}

function resolveContained(rootDir, relativePath) {
  if (
    typeof relativePath !== 'string' ||
    relativePath.length === 0 ||
    path.isAbsolute(relativePath)
  ) {
    throw new Error(`Native artifact path must be relative: ${relativePath}`);
  }
  const resolvedRoot = path.resolve(rootDir);
  const resolved = path.resolve(resolvedRoot, ...relativePath.split('/'));
  if (
    resolved !== resolvedRoot &&
    !resolved.startsWith(`${resolvedRoot}${path.sep}`)
  ) {
    throw new Error(
      `Native artifact path escapes its repository: ${relativePath}`
    );
  }
  return resolved;
}

function stableStringify(value) {
  if (Array.isArray(value)) {
    return `[${value.map((item) => stableStringify(item)).join(',')}]`;
  }
  if (value && typeof value === 'object') {
    const entries = Object.entries(value).sort(([left], [right]) =>
      left < right ? -1 : left > right ? 1 : 0
    );
    return `{${entries
      .map(([key, item]) => `${JSON.stringify(key)}:${stableStringify(item)}`)
      .join(',')}}`;
  }
  return JSON.stringify(value);
}

function sha256Bytes(bytes) {
  return crypto.createHash('sha256').update(bytes).digest('hex');
}

function sha256File(filePath) {
  return sha256Bytes(fs.readFileSync(filePath));
}

function appendDigestRecord(hash, name, bytes) {
  const nameBytes = Buffer.from(name, 'utf8');
  const length = Buffer.alloc(8);
  length.writeBigUInt64BE(BigInt(bytes.length));
  hash.update(nameBytes);
  hash.update(Buffer.from([0]));
  hash.update(length);
  hash.update(bytes);
}

export function loadNativeArtifactCatalog({ sourceRoot }) {
  const catalogPath = resolveContained(
    sourceRoot,
    NATIVE_ARTIFACT_CATALOG_PATH
  );
  const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
  if (catalog.schemaVersion !== 1) {
    throw new Error(
      `Unsupported native artifact catalog schema: ${catalog.schemaVersion}`
    );
  }
  for (const platform of Object.keys(NATIVE_RECORDER_ARTIFACTS)) {
    validateCatalogPlatform(platform, catalog.platforms?.[platform]);
  }
  return catalog;
}

function validateCatalogPlatform(platform, policy) {
  if (
    !policy ||
    !Array.isArray(policy.inputs) ||
    !Array.isArray(policy.artifacts)
  ) {
    throw new Error(`Native artifact catalog is missing platform ${platform}`);
  }
  const expected = NATIVE_RECORDER_ARTIFACTS[platform];
  if (policy.artifacts.length !== expected.length) {
    throw new Error(
      `Native artifact catalog has the wrong ${platform} artifact count`
    );
  }
  const seenInputs = new Set();
  for (const relativePath of policy.inputs) {
    if (seenInputs.has(relativePath)) {
      throw new Error(`Duplicate ${platform} native input: ${relativePath}`);
    }
    seenInputs.add(relativePath);
  }
  for (const expectedArtifact of expected) {
    const artifact = policy.artifacts.find(
      ({ id }) => id === expectedArtifact.id
    );
    if (!artifact) {
      throw new Error(
        `Native artifact catalog is missing ${expectedArtifact.id}`
      );
    }
    if (
      artifact.kind !== expectedArtifact.kind ||
      artifact.destinationPath !== expectedArtifact.destinationPath
    ) {
      throw new Error(
        `Native artifact catalog changed installer ownership for ${artifact.id}`
      );
    }
    for (const key of ['buildScript', 'outputPath', 'binaryPath']) {
      if (typeof artifact[key] !== 'string' || artifact[key].length === 0) {
        throw new Error(`Native artifact ${artifact.id} is missing ${key}`);
      }
    }
    if (
      !Array.isArray(artifact.requiredExports) ||
      artifact.requiredExports.length === 0
    ) {
      throw new Error(
        `Native artifact ${artifact.id} has no ABI export contract`
      );
    }
  }
}

export function computePlatformRequirement({ sourceRoot, platform, catalog }) {
  assertPlatform(platform);
  const resolvedCatalog = catalog ?? loadNativeArtifactCatalog({ sourceRoot });
  const policy = resolvedCatalog.platforms[platform];
  const hash = crypto.createHash('sha256');
  hash.update(inputDigestDomain);
  appendDigestRecord(
    hash,
    '@catalog-schema',
    Buffer.from(String(resolvedCatalog.schemaVersion), 'utf8')
  );
  appendDigestRecord(
    hash,
    '@platform-policy',
    Buffer.from(stableStringify(policy), 'utf8')
  );

  const inputFiles = {};
  for (const relativePath of [...policy.inputs].sort()) {
    const filePath = resolveContained(sourceRoot, relativePath);
    const stat = fs.lstatSync(filePath, { throwIfNoEntry: false });
    if (!stat?.isFile() || stat.isSymbolicLink()) {
      throw new Error(`Native input must be a regular file: ${relativePath}`);
    }
    const bytes = fs.readFileSync(filePath);
    inputFiles[relativePath] = sha256Bytes(bytes);
    appendDigestRecord(hash, relativePath, bytes);
  }

  return {
    inputDigest: `sha256:${hash.digest('hex')}`,
    inputFiles,
    policy: {
      architecture: policy.architecture,
      ...(policy.deploymentTarget
        ? { deploymentTarget: policy.deploymentTarget }
        : {}),
      signing: policy.signing
    }
  };
}

function snapshotAbsoluteArtifact(artifactPath, kind) {
  const stat = fs.lstatSync(artifactPath, { throwIfNoEntry: false });
  if (!stat) throw new Error(`Missing native recorder input: ${artifactPath}`);
  if (stat.isSymbolicLink()) {
    throw new Error(
      `Native recorder inputs cannot contain symlinks: ${artifactPath}`
    );
  }
  if (kind === 'file') {
    if (!stat.isFile())
      throw new Error(`Expected native recorder file: ${artifactPath}`);
    return { kind, size: stat.size, sha256: sha256File(artifactPath) };
  }
  if (kind !== 'directory' || !stat.isDirectory()) {
    throw new Error(`Expected native recorder directory: ${artifactPath}`);
  }

  const entries = {};
  const walk = (absoluteDir, relativeDir) => {
    for (const dirent of fs
      .readdirSync(absoluteDir, { withFileTypes: true })
      .sort((left, right) =>
        left.name < right.name ? -1 : left.name > right.name ? 1 : 0
      )) {
      const absolute = path.join(absoluteDir, dirent.name);
      const relative = relativeDir
        ? `${relativeDir}/${dirent.name}`
        : dirent.name;
      if (dirent.isSymbolicLink()) {
        throw new Error(
          `Native recorder inputs cannot contain symlinks: ${absolute}`
        );
      }
      if (dirent.isDirectory()) {
        entries[relative] = { kind: 'directory' };
        walk(absolute, relative);
      } else if (dirent.isFile()) {
        const fileStat = fs.statSync(absolute);
        entries[relative] = {
          kind: 'file',
          size: fileStat.size,
          sha256: sha256File(absolute)
        };
      } else {
        throw new Error(`Unsupported native recorder input type: ${absolute}`);
      }
    }
  };
  walk(artifactPath, '');
  return { kind, entries };
}

function snapshotInstallerArtifact(rootDir, expectedArtifact) {
  return snapshotAbsoluteArtifact(
    resolveContained(rootDir, expectedArtifact.destinationPath),
    expectedArtifact.kind
  );
}

function assertArtifactSnapshotMatches(expected, actual, label) {
  if (stableStringify(expected) !== stableStringify(actual)) {
    throw new Error(`Native recorder input drifted for ${label}`);
  }
}

function verifyPlatformArtifacts({ rootDir, platform, platformRecord }) {
  if (!platformRecord || typeof platformRecord !== 'object') {
    throw new Error(`Native recorder manifest is missing platform ${platform}`);
  }
  for (const expectedArtifact of NATIVE_RECORDER_ARTIFACTS[platform]) {
    const record = platformRecord.artifacts?.[expectedArtifact.id];
    if (!record) {
      throw new Error(
        `Native recorder manifest is missing ${expectedArtifact.id}`
      );
    }
    if (record.destinationPath !== expectedArtifact.destinationPath) {
      throw new Error(
        `Native recorder destination drifted for ${expectedArtifact.id}`
      );
    }
    const actual = snapshotInstallerArtifact(rootDir, expectedArtifact);
    assertArtifactSnapshotMatches(
      record.content,
      actual,
      expectedArtifact.destinationPath
    );
  }
}

function readManifest(lockPath) {
  const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  if (lock.schemaVersion !== 3) {
    throw new Error(
      `Unsupported native recorder input lock schema: ${lock.schemaVersion}`
    );
  }
  if (lock.sourceRepository !== expectedRepository) {
    throw new Error(
      `Native recorder source repository must be ${expectedRepository}`
    );
  }
  return lock;
}

export function verifyNativeRecorderInput({
  rootDir,
  platforms = Object.keys(NATIVE_RECORDER_ARTIFACTS),
  lockPath = path.join(rootDir, NATIVE_RECORDER_LOCK_PATH)
}) {
  const lock = readManifest(lockPath);
  for (const platform of platforms) {
    assertPlatform(platform);
    verifyPlatformArtifacts({
      rootDir,
      platform,
      platformRecord: lock.platforms?.[platform]
    });
  }
  return { sourceRepository: lock.sourceRepository, platforms };
}

function loadManifestForPromotion(lockPath) {
  if (!fs.existsSync(lockPath)) {
    return {
      schemaVersion: 3,
      sourceRepository: expectedRepository,
      platforms: {}
    };
  }
  const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  if (lock.schemaVersion === 3) {
    if (lock.sourceRepository !== expectedRepository) {
      throw new Error(
        `Native recorder source repository must be ${expectedRepository}`
      );
    }
    return lock;
  }
  throw new Error(
    `Unsupported native recorder input lock schema: ${lock.schemaVersion}`
  );
}

function producerProvenance(sourceRoot) {
  const commit = execFileSync('git', ['rev-parse', 'HEAD'], {
    cwd: sourceRoot,
    encoding: 'utf8'
  }).trim();
  const dirty =
    execFileSync('git', ['status', '--porcelain', '--untracked-files=all'], {
      cwd: sourceRoot,
      encoding: 'utf8'
    }).trim().length > 0;
  return { repository: expectedRepository, commit, dirty };
}

function builtArtifactPath(buildRoot, artifact) {
  return resolveContained(buildRoot, `${artifact.id}/${artifact.outputPath}`);
}

function writeManifestTransaction({
  rootDir,
  lockPath,
  manifest,
  replacements
}) {
  const transactionId = `${process.pid}-${crypto.randomUUID()}`;
  const prepared = [];
  let lockBackup = null;
  let lockInstalled = false;
  const lockTemp = `${lockPath}.bpp-stage-${transactionId}`;

  try {
    for (const replacement of replacements) {
      const destination = resolveContained(
        rootDir,
        replacement.destinationPath
      );
      const parent = path.dirname(destination);
      fs.mkdirSync(parent, { recursive: true });
      const basename = path.basename(destination);
      const staged = path.join(
        parent,
        `.${basename}.bpp-stage-${transactionId}`
      );
      const backup = path.join(
        parent,
        `.${basename}.bpp-backup-${transactionId}`
      );
      fs.cpSync(replacement.source, staged, {
        recursive: replacement.kind === 'directory',
        errorOnExist: true,
        force: false,
        preserveTimestamps: false
      });
      const stagedSnapshot = snapshotAbsoluteArtifact(staged, replacement.kind);
      assertArtifactSnapshotMatches(
        replacement.content,
        stagedSnapshot,
        replacement.destinationPath
      );
      prepared.push({
        ...replacement,
        destination,
        staged,
        backup,
        installed: false
      });
    }

    for (const item of prepared) {
      if (fs.existsSync(item.destination))
        fs.renameSync(item.destination, item.backup);
      fs.renameSync(item.staged, item.destination);
      item.installed = true;
    }

    fs.mkdirSync(path.dirname(lockPath), { recursive: true });
    fs.writeFileSync(lockTemp, `${JSON.stringify(manifest, null, 2)}\n`);
    if (fs.existsSync(lockPath)) {
      lockBackup = `${lockPath}.bpp-backup-${transactionId}`;
      fs.renameSync(lockPath, lockBackup);
    }
    fs.renameSync(lockTemp, lockPath);
    lockInstalled = true;
  } catch (error) {
    if (lockInstalled && fs.existsSync(lockPath))
      fs.rmSync(lockPath, { force: true });
    if (lockBackup && fs.existsSync(lockBackup))
      fs.renameSync(lockBackup, lockPath);
    for (const item of [...prepared].reverse()) {
      if (item.installed && fs.existsSync(item.destination)) {
        fs.rmSync(item.destination, { recursive: true, force: true });
      }
      if (fs.existsSync(item.backup))
        fs.renameSync(item.backup, item.destination);
      if (fs.existsSync(item.staged))
        fs.rmSync(item.staged, { recursive: true, force: true });
    }
    if (fs.existsSync(lockTemp)) fs.rmSync(lockTemp, { force: true });
    throw error;
  }

  for (const item of prepared) {
    try {
      if (fs.existsSync(item.backup)) {
        fs.rmSync(item.backup, { recursive: true, force: true });
      }
    } catch (error) {
      console.warn(
        `native-recorder-input: retained backup ${item.backup}: ${error}`
      );
    }
  }
  try {
    if (lockBackup && fs.existsSync(lockBackup))
      fs.rmSync(lockBackup, { force: true });
  } catch (error) {
    console.warn(
      `native-recorder-input: retained backup ${lockBackup}: ${error}`
    );
  }
}

export function promoteNativeRecorderInput({
  rootDir,
  sourceRoot,
  buildRoot,
  platform,
  expectedInputDigest,
  lockPath = path.join(rootDir, NATIVE_RECORDER_LOCK_PATH)
}) {
  assertPlatform(platform);
  const catalog = loadNativeArtifactCatalog({ sourceRoot });
  const policy = catalog.platforms[platform];
  const requirement = computePlatformRequirement({
    sourceRoot,
    platform,
    catalog
  });
  if (expectedInputDigest && requirement.inputDigest !== expectedInputDigest) {
    throw new Error(
      `Native ${platform} inputs changed while the producer build was running`
    );
  }
  const replacements = policy.artifacts.map((artifact) => {
    const source = builtArtifactPath(buildRoot, artifact);
    return {
      id: artifact.id,
      kind: artifact.kind,
      destinationPath: artifact.destinationPath,
      source,
      content: snapshotAbsoluteArtifact(source, artifact.kind)
    };
  });

  const manifest = loadManifestForPromotion(lockPath);
  manifest.platforms[platform] = {
    ...requirement,
    producer: producerProvenance(sourceRoot),
    artifacts: Object.fromEntries(
      replacements.map((replacement) => [
        replacement.id,
        {
          destinationPath: replacement.destinationPath,
          content: replacement.content
        }
      ])
    )
  };
  writeManifestTransaction({ rootDir, lockPath, manifest, replacements });
  verifyNativeRecorderInput({ rootDir, platforms: [platform], lockPath });
  return manifest.platforms[platform];
}

export function nativeRecorderStatus({
  rootDir,
  sourceRoot,
  platform,
  lockPath = path.join(rootDir, NATIVE_RECORDER_LOCK_PATH)
}) {
  assertPlatform(platform);
  const requirement = computePlatformRequirement({ sourceRoot, platform });
  if (!fs.existsSync(lockPath)) {
    return { fresh: false, reason: 'manifest is missing', requirement };
  }
  const raw = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  if (raw.schemaVersion !== 3) {
    throw new Error(
      `Unsupported native recorder input lock schema: ${raw.schemaVersion}`
    );
  }
  const platformRecord = raw.platforms?.[platform];
  if (platformRecord?.inputDigest !== requirement.inputDigest) {
    return {
      fresh: false,
      reason: 'canonical input digest changed',
      requirement
    };
  }
  try {
    verifyPlatformArtifacts({ rootDir, platform, platformRecord });
  } catch (error) {
    return {
      fresh: false,
      reason: error instanceof Error ? error.message : String(error),
      requirement
    };
  }
  return {
    fresh: true,
    reason: 'input digest and staged artifacts match',
    requirement
  };
}

function runNativeBuild({ sourceRoot, platform, policy, buildRoot }) {
  for (const artifact of policy.artifacts) {
    const outputDirectory = resolveContained(buildRoot, artifact.id);
    fs.mkdirSync(outputDirectory, { recursive: true });
    const buildScript = resolveContained(sourceRoot, artifact.buildScript);
    const env = {
      ...process.env,
      BPP_NATIVE_REQUIRED_EXPORTS: artifact.requiredExports.join('\n'),
      BPP_NATIVE_REQUIRED_WEAK_IMPORTS: (
        artifact.requiredWeakImports ?? []
      ).join('\n'),
      BPP_NATIVE_ALLOWED_DEPENDENCY_PREFIXES: (
        artifact.allowedDependencyPrefixes ?? []
      ).join('\n'),
      BPP_NATIVE_ALLOWED_DEPENDENCIES: (
        artifact.allowedDependencies ?? []
      ).join('\n')
    };
    const command =
      platform === 'macos'
        ? { file: 'bash', args: [buildScript, outputDirectory] }
        : {
            file: 'powershell.exe',
            args: [
              '-NoLogo',
              '-NoProfile',
              '-ExecutionPolicy',
              'Bypass',
              '-File',
              buildScript,
              '-OutputDirectory',
              outputDirectory
            ]
          };
    execFileSync(command.file, command.args, {
      cwd: sourceRoot,
      env,
      stdio: 'inherit'
    });
    snapshotAbsoluteArtifact(
      builtArtifactPath(buildRoot, artifact),
      artifact.kind
    );
  }
}

export function ensureNativeRecorderInput({ rootDir, sourceRoot, platform }) {
  if (
    (platform === 'macos' && process.platform !== 'darwin') ||
    (platform === 'windows' && process.platform !== 'win32')
  ) {
    throw new Error(
      `Cannot build ${platform} native inputs on ${process.platform}`
    );
  }
  const status = nativeRecorderStatus({ rootDir, sourceRoot, platform });
  if (status.fresh) return { ...status, rebuilt: false };

  const catalog = loadNativeArtifactCatalog({ sourceRoot });
  const buildRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), `bpp-native-${platform}-`)
  );
  try {
    runNativeBuild({
      sourceRoot,
      platform,
      policy: catalog.platforms[platform],
      buildRoot
    });
    promoteNativeRecorderInput({
      rootDir,
      sourceRoot,
      buildRoot,
      platform,
      expectedInputDigest: status.requirement.inputDigest
    });
  } finally {
    fs.rmSync(buildRoot, { recursive: true, force: true });
  }
  const promoted = nativeRecorderStatus({ rootDir, sourceRoot, platform });
  if (!promoted.fresh) {
    throw new Error(
      `Native ${platform} promotion did not become fresh: ${promoted.reason}`
    );
  }
  return { ...promoted, rebuilt: true, previousReason: status.reason };
}

function parseCli(args) {
  const command =
    args[0] === 'ensure' || args[0] === 'verify' ? args.shift() : 'verify';
  let platform;
  let sourceRoot;
  while (args.length > 0) {
    const argument = args.shift();
    if (argument === '--platform') platform = args.shift();
    else if (argument === '--source-root') sourceRoot = args.shift();
    else throw new Error(`Unknown native recorder input argument: ${argument}`);
  }
  return { command, platform, sourceRoot };
}

function main() {
  const rootDir = path.resolve(import.meta.dirname, '..', '..');
  const { command, platform, sourceRoot } = parseCli(process.argv.slice(2));
  if (command === 'ensure') {
    if (!platform || !sourceRoot) {
      throw new Error('ensure requires --platform and --source-root');
    }
    const result = ensureNativeRecorderInput({
      rootDir,
      sourceRoot: path.resolve(sourceRoot),
      platform
    });
    console.log(
      result.rebuilt
        ? `native-recorder-input: rebuilt and promoted ${platform} (${result.previousReason})`
        : `native-recorder-input: reused fresh ${platform} inputs`
    );
    return;
  }

  const platforms = platform
    ? [platform]
    : Object.keys(NATIVE_RECORDER_ARTIFACTS);
  verifyNativeRecorderInput({ rootDir, platforms });
  console.log(`native-recorder-input: verified ${platforms.join(', ')}`);
}

if (import.meta.main) {
  try {
    main();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`native-recorder-input: ${message}`);
    process.exit(1);
  }
}
