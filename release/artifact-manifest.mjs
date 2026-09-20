import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { runGit } from '../scripts/git-command.mjs';
import { RELEASE_PLATFORMS } from './release-platforms.mjs';

const releasePaths = [
  '.',
  '../VERSION',
  '../release.mjs',
  '../release',
  '../scripts/git-command.mjs',
  '../bazaarplusplus-mod'
];

function platformDefinition(buildPlatform) {
  const matches = RELEASE_PLATFORMS.filter(
    (platform) => platform.buildPlatform === buildPlatform
  );
  if (matches.length !== 1) {
    throw new Error(`Unsupported artifact platform: ${buildPlatform}`);
  }
  return matches[0];
}

function sha256(buffer) {
  return crypto.createHash('sha256').update(buffer).digest('hex');
}

export function gitStateForRoot(rootDir) {
  const commit = runGit(['rev-parse', 'HEAD'], { cwd: rootDir }).trim();
  // Include the product's producer and shared release inputs, but not unrelated
  // website/analyzer work or local root tooling.
  const status = runGit(
    ['status', '--porcelain', '--untracked-files=all', '--', ...releasePaths],
    { cwd: rootDir }
  ).trim();
  return { commit, dirty: status.length > 0 };
}

export function releaseSourceIdentity(rootDir) {
  const files = [
    ...new Set(
      runGit(
        [
          'ls-files',
          '--cached',
          '--others',
          '--exclude-standard',
          '-z',
          '--',
          ...releasePaths
        ],
        { cwd: rootDir }
      )
        .split('\0')
        .filter(Boolean)
    )
  ].sort();
  const inputs = files.map((name) => {
    const file = path.resolve(rootDir, name);
    const stat = fs.lstatSync(file, { throwIfNoEntry: false });
    const bytes = stat?.isSymbolicLink()
      ? fs.readlinkSync(file)
      : stat?.isFile()
        ? fs.readFileSync(file)
        : '';
    return [name, stat ? sha256(bytes) : null];
  });
  return {
    ...gitStateForRoot(rootDir),
    digest: sha256(JSON.stringify(inputs))
  };
}

function relativeArtifactPath(rootDir, filePath) {
  const relativePath = path.relative(rootDir, filePath);
  if (
    !relativePath ||
    relativePath === '..' ||
    relativePath.startsWith(`..${path.sep}`) ||
    path.isAbsolute(relativePath)
  ) {
    throw new Error(`Artifact path escapes the project root: ${filePath}`);
  }
  return relativePath.split(path.sep).join('/');
}

function artifactRecord(rootDir, filePath) {
  const stat = fs.statSync(filePath, { throwIfNoEntry: false });
  if (!stat?.isFile()) throw new Error(`Missing artifact: ${filePath}`);
  const content = fs.readFileSync(filePath);
  return {
    path: relativeArtifactPath(rootDir, filePath),
    size: stat.size,
    sha256: sha256(content)
  };
}

function recursivelyFindFiles(rootDir, predicate) {
  if (!fs.statSync(rootDir, { throwIfNoEntry: false })?.isDirectory())
    return [];
  const matches = [];
  const walk = (directory) => {
    for (const dirent of fs.readdirSync(directory, { withFileTypes: true })) {
      const child = path.join(directory, dirent.name);
      if (dirent.isDirectory()) walk(child);
      else if (dirent.isFile() && predicate(child)) matches.push(child);
    }
  };
  walk(rootDir);
  return matches.sort();
}

function exactlyOne(candidates, description, platform) {
  if (candidates.length === 0) {
    throw new Error(`No ${description} found for ${platform}`);
  }
  if (candidates.length > 1) {
    throw new Error(
      `Multiple ${description}s found for ${platform}:\n${candidates.map((candidate) => `- ${candidate}`).join('\n')}`
    );
  }
  return candidates[0];
}

function assertFileNameContainsVersion(filePath, version) {
  if (!path.basename(filePath).includes(version)) {
    throw new Error(
      `Artifact version mismatch: ${filePath} does not contain ${version}`
    );
  }
}

function matchesInstallerGlob(fileName, glob) {
  if (!glob.startsWith('*'))
    throw new Error(`Unsupported installer glob: ${glob}`);
  return fileName.endsWith(glob.slice(1));
}

function discoverArtifacts(rootDir, platform) {
  const installerDirectory = path.join(rootDir, platform.installerDir);
  const installerCandidates = fs
    .readdirSync(installerDirectory, { withFileTypes: true })
    .filter(
      (dirent) =>
        dirent.isFile() &&
        matchesInstallerGlob(dirent.name, platform.installerNameGlob) &&
        !dirent.name.endsWith('.sig')
    )
    .map((dirent) => path.join(installerDirectory, dirent.name))
    .sort();
  const installer = exactlyOne(
    installerCandidates,
    'installer artifact',
    platform.buildPlatform
  );
  const signature = exactlyOne(
    recursivelyFindFiles(path.join(rootDir, platform.bundleRoot), (filePath) =>
      filePath.endsWith('.sig')
    ),
    'updater signature',
    platform.buildPlatform
  );
  const updater = signature.slice(0, -'.sig'.length);
  if (!fs.statSync(updater, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(`Missing updater artifact for signature: ${signature}`);
  }
  return { installer, updater, signature };
}

export function artifactManifestPath(rootDir, platform) {
  return path.join(
    rootDir,
    'src-tauri',
    'target',
    'release-artifacts',
    `${platform}.json`
  );
}

export function createArtifactManifest({
  rootDir,
  platform,
  version,
  gitState = gitStateForRoot(rootDir),
  builtAt = new Date()
}) {
  const definition = platformDefinition(platform);
  const discovered = discoverArtifacts(rootDir, definition);
  assertFileNameContainsVersion(discovered.installer, version);
  const signatureContent = fs.readFileSync(discovered.signature, 'utf8').trim();
  if (!signatureContent) throw new Error('Updater signature is empty');

  const manifest = {
    schemaVersion: 2,
    appVersion: version,
    buildPlatform: platform,
    releasePlatformKey: definition.key,
    gitCommit: gitState.commit,
    dirty: gitState.dirty,
    payloadBuild: artifactRecord(
      rootDir,
      path.join(
        rootDir,
        'src-tauri/resources/BepInExSource',
        platform,
        'payload-build.json'
      )
    ),
    installer: artifactRecord(rootDir, discovered.installer),
    updater: artifactRecord(rootDir, discovered.updater),
    signature: {
      ...artifactRecord(rootDir, discovered.signature),
      content: signatureContent
    },
    builtAt: builtAt.toISOString()
  };
  const manifestPath = artifactManifestPath(rootDir, platform);
  fs.mkdirSync(path.dirname(manifestPath), { recursive: true });
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  return { manifest, manifestPath };
}

function resolveManifestArtifact(rootDir, record) {
  if (
    !record ||
    typeof record.path !== 'string' ||
    path.isAbsolute(record.path)
  ) {
    throw new Error('Artifact manifest contains an invalid path');
  }
  const resolved = path.resolve(rootDir, ...record.path.split('/'));
  const rootPrefix = `${path.resolve(rootDir)}${path.sep}`;
  if (!resolved.startsWith(rootPrefix)) {
    throw new Error(
      `Artifact manifest path escapes the project root: ${record.path}`
    );
  }
  return resolved;
}

function validateRecord(rootDir, label, record) {
  const filePath = resolveManifestArtifact(rootDir, record);
  const stat = fs.statSync(filePath, { throwIfNoEntry: false });
  if (!stat?.isFile())
    throw new Error(`Missing artifact from manifest: ${filePath}`);
  const digest = sha256(fs.readFileSync(filePath));
  if (stat.size !== record.size || digest !== record.sha256) {
    throw new Error(
      `${label} size or SHA-256 does not match artifact manifest`
    );
  }
  return filePath;
}

export function validateArtifactManifest({
  rootDir,
  manifestPath,
  platform,
  version,
  gitState = gitStateForRoot(rootDir)
}) {
  if (!fs.statSync(manifestPath, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(`Missing artifact manifest: ${manifestPath}`);
  }
  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  const definition = platformDefinition(platform);
  if (manifest.schemaVersion !== 2)
    throw new Error('Unsupported artifact manifest schema');
  if (
    manifest.appVersion !== version ||
    manifest.buildPlatform !== platform ||
    manifest.releasePlatformKey !== definition.key
  ) {
    throw new Error(
      `Artifact manifest version mismatch or platform mismatch: expected ${version}/${definition.key}`
    );
  }
  if (manifest.gitCommit !== gitState.commit) {
    throw new Error(
      `Artifact manifest commit mismatch: built ${manifest.gitCommit}, current ${gitState.commit}`
    );
  }
  if (manifest.dirty)
    throw new Error('Artifact manifest records a dirty build');
  if (gitState.dirty)
    throw new Error('Current checkout is dirty; refusing upload');

  const payloadBuild = validateRecord(
    rootDir,
    'Payload build',
    manifest.payloadBuild
  );
  const payloadIdentity = JSON.parse(fs.readFileSync(payloadBuild, 'utf8'));
  if (
    payloadIdentity.schemaVersion !== 2 ||
    payloadIdentity.productVersion !== version ||
    payloadIdentity.platform !== platform
  ) {
    throw new Error('Payload build identity does not match the installer');
  }

  const installer = validateRecord(rootDir, 'Installer', manifest.installer);
  const updater = validateRecord(rootDir, 'Updater', manifest.updater);
  const signature = validateRecord(rootDir, 'Signature', manifest.signature);
  if (
    fs.readFileSync(signature, 'utf8').trim() !== manifest.signature.content
  ) {
    throw new Error('Signature content does not match artifact manifest');
  }
  assertFileNameContainsVersion(installer, version);
  return { manifest, installer, updater, signature };
}

if (import.meta.main) {
  console.error('Usage: create artifacts through node release.mjs build');
  process.exitCode = 1;
}
