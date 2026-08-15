import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import zlib from 'node:zlib';
import { resolveBuildPlatform } from './release-platforms.mjs';

// This is the first mod version guaranteed to write the BazaarPlusPlusV5 data root.
export const V5_MIN_MOD_VERSION = '4.7.0';

export const REQUIRED_RELEASE_INPUTS = Object.freeze({
  macos: Object.freeze([
    'libdoorstop.dylib',
    'BepInEx/plugins/BazaarPlusPlus.dll',
    'BepInEx/plugins/BazaarPlusPlus.ModApi.dll',
    'BepInEx/plugins/BazaarPlusPlus.Storage.dll',
    'BepInEx/plugins/BazaarPlusPlus.Localization.dll',
    'BepInEx/plugins/BazaarPlusPlus.version',
    'BepInEx/plugins/libBppMacAudio.dylib',
    'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/Info.plist',
    'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox',
    'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/_CodeSignature/CodeResources'
  ]),
  windows: Object.freeze([
    'BepInEx/plugins/BazaarPlusPlus.dll',
    'BepInEx/plugins/BazaarPlusPlus.ModApi.dll',
    'BepInEx/plugins/BazaarPlusPlus.Storage.dll',
    'BepInEx/plugins/BazaarPlusPlus.Localization.dll',
    'BepInEx/plugins/BazaarPlusPlus.version',
    'TheBazaar_Data/Plugins/x86_64/GfxPluginBppReplayMediaFoundation.dll'
  ])
});

const FORBIDDEN_RELEASE_INPUTS = Object.freeze({
  macos: Object.freeze([
    'run_bepinex.sh',
    'bpp_launcher.c',
    'BepInEx/plugins/ffmpeg',
    'BepInEx/plugins/ffmpeg-LICENSE.txt',
    'BepInEx/plugins/BppReplayRecorder.app'
  ]),
  windows: Object.freeze([
    'BepInEx/plugins/ffmpeg.exe',
    'BepInEx/plugins/ffmpeg-LICENSE.txt'
  ])
});

const osArtifactNames = new Set(['.DS_Store', 'Thumbs.db', 'desktop.ini']);
const macosExecutablePaths = new Set([
  'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox'
]);
const fixedDosDate = (1 << 5) | 1;
const fixedDosTime = 0;

const crcTable = Array.from({ length: 256 }, (_, value) => {
  let crc = value;
  for (let bit = 0; bit < 8; bit += 1) {
    crc = (crc & 1) !== 0 ? 0xedb88320 ^ (crc >>> 1) : crc >>> 1;
  }
  return crc >>> 0;
});

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function sha256(buffer) {
  return crypto.createHash('sha256').update(buffer).digest('hex');
}

function platformPaths(rootDir, platform) {
  const resources = path.join(rootDir, 'src-tauri', 'resources');
  const outputDir = path.join(resources, 'BepInExSource', platform);
  return {
    sourceDir: path.join(resources, 'SourceForBuild', platform),
    zipPath: path.join(outputDir, 'BepInEx.zip'),
    manifestPath: path.join(outputDir, 'BepInEx.zip.manifest.json')
  };
}

function payloadFileMode({ hostPlatform, platform, relativePath, sourceMode }) {
  if (hostPlatform !== 'win32') return sourceMode & 0o777;
  return platform === 'macos' && macosExecutablePaths.has(relativePath)
    ? 0o755
    : 0o644;
}

export function listPayloadFiles(
  sourceDir,
  { platform, hostPlatform = process.platform } = {}
) {
  const files = [];
  const walk = (directory) => {
    for (const dirent of fs
      .readdirSync(directory, { withFileTypes: true })
      .sort((left, right) => left.name.localeCompare(right.name))) {
      const absolutePath = path.join(directory, dirent.name);
      if (osArtifactNames.has(dirent.name)) {
        throw new Error(
          `OS artifact is forbidden in release staging: ${absolutePath}`
        );
      }
      if (dirent.isSymbolicLink()) {
        throw new Error(
          `Symbolic links are forbidden in release staging: ${absolutePath}`
        );
      }
      if (dirent.isDirectory()) {
        walk(absolutePath);
        continue;
      }
      if (!dirent.isFile()) {
        throw new Error(`Unsupported release staging entry: ${absolutePath}`);
      }
      const stat = fs.statSync(absolutePath);
      const relativePath = path
        .relative(sourceDir, absolutePath)
        .split(path.sep)
        .join('/');
      files.push({
        path: relativePath,
        absolutePath,
        mode: payloadFileMode({
          hostPlatform,
          platform,
          relativePath,
          sourceMode: stat.mode
        })
      });
    }
  };
  walk(sourceDir);
  return files.sort((left, right) => left.path.localeCompare(right.path));
}

function assertRequiredStagingInputs(sourceDir, requiredStagingPaths) {
  const missing = requiredStagingPaths.filter(
    (relativePath) =>
      !fs
        .statSync(path.join(sourceDir, relativePath), { throwIfNoEntry: false })
        ?.isFile()
  );
  if (missing.length === 0) return;

  const details = missing
    .map((relativePath) => `- ${path.join(sourceDir, relativePath)}`)
    .join('\n');
  const publishGuidance = missing.includes(
    'BepInEx/plugins/BazaarPlusPlus.version'
  )
    ? '\nRun ./run.sh publish in the mod repository first to re-stage both macOS and Windows SourceForBuild payloads.'
    : '';
  throw new Error(
    `Missing release staging inputs under SourceForBuild; provide every external/private file before preparing resources:\n${details}${publishGuidance}`
  );
}

function parseProdModVersion(content) {
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.prod$/.exec(
    content.trim()
  );
  return match?.slice(1).map(Number) ?? null;
}

function compareVersionParts(left, right) {
  for (let index = 0; index < left.length; index += 1) {
    if (left[index] !== right[index]) return left[index] - right[index];
  }
  return 0;
}

function assertStagedModWritesV5DataRoot(sourceDir) {
  const versionPath = path.join(
    sourceDir,
    'BepInEx',
    'plugins',
    'BazaarPlusPlus.version'
  );
  const content = fs.readFileSync(versionPath, 'utf8');
  const stagedVersion = parseProdModVersion(content);
  if (!stagedVersion) {
    throw new Error(
      `Cannot parse staged BazaarPlusPlus mod version '${content.trim()}' at ${versionPath}; expected {semver}.prod. Run ./run.sh publish in the mod repository first to re-stage both macOS and Windows SourceForBuild payloads.`
    );
  }

  const minimumVersion = V5_MIN_MOD_VERSION.split('.').map(Number);
  if (compareVersionParts(stagedVersion, minimumVersion) >= 0) return;

  throw new Error(
    `Staged BazaarPlusPlus mod version must be ${V5_MIN_MOD_VERSION}.prod or newer to write the BazaarPlusPlusV5 data root (found '${content.trim()}' at ${versionPath}). Run ./run.sh publish in the mod repository first to re-stage both macOS and Windows SourceForBuild payloads.`
  );
}

function assertForbiddenStagingInputs(platform, sourceDir) {
  const present = (FORBIDDEN_RELEASE_INPUTS[platform] ?? []).filter(
    (relativePath) =>
      fs.statSync(path.join(sourceDir, relativePath), {
        throwIfNoEntry: false
      }) != null
  );
  if (present.length === 0) return;

  throw new Error(
    `${platform} release staging contains retired runtime dependencies: ${present.join(', ')}`
  );
}

function assertMacosExecutableModes(platform, files) {
  // Windows does not expose Unix execute bits through stat/chmod. The same source is checked
  // authoritatively on the macOS release host before packaging.
  if (platform !== 'macos' || process.platform === 'win32') return;
  for (const requiredExecutable of [
    'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox'
  ]) {
    const file = files.find((entry) => entry.path === requiredExecutable);
    if (file && (file.mode & 0o111) === 0) {
      throw new Error(
        `macOS release staging file must be executable: ${file.absolutePath}`
      );
    }
  }
}

export function buildZipBuffer(inputEntries) {
  const entries = [...inputEntries].sort((left, right) =>
    left.name.localeCompare(right.name)
  );
  const localParts = [];
  const centralParts = [];
  let localOffset = 0;

  for (const entry of entries) {
    const name = Buffer.from(entry.name, 'utf8');
    const data = Buffer.from(entry.data);
    const isDirectory = entry.isDirectory ?? entry.name.endsWith('/');
    const method = isDirectory ? 0 : 8;
    const compressed = isDirectory
      ? Buffer.alloc(0)
      : zlib.deflateRawSync(data, { level: 9 });
    const checksum = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0, 6);
    local.writeUInt16LE(method, 8);
    local.writeUInt16LE(fixedDosTime, 10);
    local.writeUInt16LE(fixedDosDate, 12);
    local.writeUInt32LE(checksum, 14);
    local.writeUInt32LE(compressed.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(name.length, 26);
    local.writeUInt16LE(0, 28);
    localParts.push(local, name, compressed);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE((3 << 8) | 20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0, 8);
    central.writeUInt16LE(method, 10);
    central.writeUInt16LE(fixedDosTime, 12);
    central.writeUInt16LE(fixedDosDate, 14);
    central.writeUInt32LE(checksum, 16);
    central.writeUInt32LE(compressed.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(name.length, 28);
    central.writeUInt16LE(0, 30);
    central.writeUInt16LE(0, 32);
    central.writeUInt16LE(0, 34);
    central.writeUInt16LE(0, 36);
    const fileType = isDirectory ? 0o040000 : 0o100000;
    central.writeUInt32LE(((fileType | entry.mode) << 16) >>> 0, 38);
    central.writeUInt32LE(localOffset, 42);
    centralParts.push(central, name);

    localOffset += local.length + name.length + compressed.length;
  }

  const centralDirectory = Buffer.concat(centralParts);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(0, 4);
  end.writeUInt16LE(0, 6);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(centralDirectory.length, 12);
  end.writeUInt32LE(localOffset, 16);
  end.writeUInt16LE(0, 20);
  return Buffer.concat([...localParts, centralDirectory, end]);
}

function findEndOfCentralDirectory(buffer) {
  for (let offset = buffer.length - 22; offset >= 0; offset -= 1) {
    if (buffer.readUInt32LE(offset) === 0x06054b50) return offset;
  }
  throw new Error('ZIP end-of-central-directory record not found');
}

export function readZipEntries(buffer) {
  const endOffset = findEndOfCentralDirectory(buffer);
  const centralSize = buffer.readUInt32LE(endOffset + 12);
  const centralOffset = buffer.readUInt32LE(endOffset + 16);
  const centralEnd = centralOffset + centralSize;
  const entries = [];
  let cursor = centralOffset;

  while (cursor < centralEnd) {
    if (buffer.readUInt32LE(cursor) !== 0x02014b50) {
      throw new Error(
        `Invalid ZIP central directory header at offset ${cursor}`
      );
    }
    const method = buffer.readUInt16LE(cursor + 10);
    const compressedSize = buffer.readUInt32LE(cursor + 20);
    const uncompressedSize = buffer.readUInt32LE(cursor + 24);
    const nameLength = buffer.readUInt16LE(cursor + 28);
    const extraLength = buffer.readUInt16LE(cursor + 30);
    const commentLength = buffer.readUInt16LE(cursor + 32);
    const externalAttributes = buffer.readUInt32LE(cursor + 38);
    const localHeaderOffset = buffer.readUInt32LE(cursor + 42);
    const nameStart = cursor + 46;
    const name = buffer.toString('utf8', nameStart, nameStart + nameLength);
    const localNameLength = buffer.readUInt16LE(localHeaderOffset + 26);
    const localExtraLength = buffer.readUInt16LE(localHeaderOffset + 28);
    const dataStart =
      localHeaderOffset + 30 + localNameLength + localExtraLength;
    const compressed = buffer.subarray(dataStart, dataStart + compressedSize);
    let data;
    if (method === 0) data = Buffer.from(compressed);
    else if (method === 8) data = zlib.inflateRawSync(compressed);
    else
      throw new Error(
        `Unsupported ZIP compression method ${method} for ${name}`
      );
    if (data.length !== uncompressedSize) {
      throw new Error(`ZIP size mismatch for ${name}`);
    }

    const unixMode = (externalAttributes >>> 16) & 0xffff;
    entries.push({
      name,
      isDirectory: name.endsWith('/') || (unixMode & 0o170000) === 0o040000,
      mode: unixMode & 0o777,
      data
    });
    cursor = nameStart + nameLength + extraLength + commentLength;
  }
  return entries;
}

function normalizedEntryName(entry) {
  if (entry.name.includes('\\')) {
    throw new Error(`ZIP entry contains a forbidden backslash: ${entry.name}`);
  }
  if (entry.name.startsWith('/') || /^[A-Za-z]:/.test(entry.name)) {
    throw new Error(`ZIP entry uses an unsafe absolute path: ${entry.name}`);
  }
  const segments = entry.name.replace(/\/$/, '').split('/');
  if (segments.includes('..')) {
    throw new Error(`ZIP entry contains parent traversal: ${entry.name}`);
  }
  const normalized = segments
    .filter((segment) => segment && segment !== '.')
    .join('/');
  if (!normalized)
    throw new Error(`ZIP entry has an empty normalized path: ${entry.name}`);
  if (osArtifactNames.has(normalized.split('/').at(-1))) {
    throw new Error(`ZIP entry is an OS artifact: ${entry.name}`);
  }
  return normalized;
}

function setDifference(left, right) {
  return [...left].filter((value) => !right.has(value)).sort();
}

export function validateZipEntrySet(entries, expectedPaths) {
  const files = [];
  const seen = new Map();
  for (const entry of entries) {
    const normalized = normalizedEntryName(entry);
    if (entry.isDirectory) continue;
    if (seen.has(normalized)) {
      throw new Error(
        `ZIP contains duplicate normalized path '${normalized}' from '${seen.get(normalized).name}' and '${entry.name}'`
      );
    }
    seen.set(normalized, entry);
    files.push({ normalized, entry });
  }

  const expected = new Set(expectedPaths);
  const direct = new Set(files.map(({ normalized }) => normalized));
  let selected = files;
  let actual = direct;

  const directMissing = setDifference(expected, direct);
  const directExtra = setDifference(direct, expected);
  if (directMissing.length > 0 || directExtra.length > 0) {
    const firstSegments = new Set(
      files.map(({ normalized }) => normalized.split('/')[0])
    );
    if (
      firstSegments.size === 1 &&
      files.every(({ normalized }) => normalized.includes('/'))
    ) {
      const stripped = files.map(({ normalized, entry }) => ({
        normalized: normalized.slice(normalized.indexOf('/') + 1),
        entry
      }));
      const strippedSet = new Set(stripped.map(({ normalized }) => normalized));
      if (
        setDifference(expected, strippedSet).length === 0 &&
        setDifference(strippedSet, expected).length === 0
      ) {
        selected = stripped;
        actual = strippedSet;
      }
    }
  }

  const missing = setDifference(expected, actual);
  const extra = setDifference(actual, expected);
  if (missing.length > 0 || extra.length > 0) {
    const details = [
      missing.length > 0 ? `missing: ${missing.join(', ')}` : null,
      extra.length > 0 ? `extra: ${extra.join(', ')}` : null
    ]
      .filter(Boolean)
      .join('; ');
    throw new Error(
      `ZIP payload entries do not exactly match staging (${details})`
    );
  }
  return new Map(selected.map(({ normalized, entry }) => [normalized, entry]));
}

export function preparePayloadZip({
  rootDir,
  platform,
  hostPlatform = process.platform,
  requiredStagingPaths = REQUIRED_RELEASE_INPUTS[platform]
}) {
  if (!requiredStagingPaths)
    throw new Error(`Unsupported payload platform: ${platform}`);
  const { sourceDir, zipPath, manifestPath } = platformPaths(rootDir, platform);
  assertRequiredStagingInputs(sourceDir, requiredStagingPaths);
  assertStagedModWritesV5DataRoot(sourceDir);
  assertForbiddenStagingInputs(platform, sourceDir);
  return writeDeterministicZip({
    sourceDir,
    outputPath: zipPath,
    manifestPath,
    platform,
    hostPlatform
  });
}

export function writeDeterministicZip({
  sourceDir,
  outputPath,
  manifestPath,
  platform,
  hostPlatform = process.platform
}) {
  if (platform) assertForbiddenStagingInputs(platform, sourceDir);
  const files = listPayloadFiles(sourceDir, { platform, hostPlatform });
  if (platform) assertMacosExecutableModes(platform, files);
  const entries = files.map((file) => ({
    name: file.path,
    data: fs.readFileSync(file.absolutePath),
    mode: file.mode
  }));
  const buffer = buildZipBuffer(entries);
  const manifest = manifestPath
    ? {
        schemaVersion: 1,
        platform,
        zipSha256: sha256(buffer),
        entries: entries.map((entry) => ({
          path: entry.name,
          size: entry.data.length,
          sha256: sha256(entry.data),
          mode: entry.mode
        }))
      }
    : undefined;
  if (manifestPath && !platform) {
    throw new Error('A payload platform is required when writing a manifest');
  }

  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  if (manifestPath)
    fs.mkdirSync(path.dirname(manifestPath), { recursive: true });
  const tempZip = `${outputPath}.tmp-${process.pid}`;
  const tempManifest = manifestPath
    ? `${manifestPath}.tmp-${process.pid}`
    : undefined;
  try {
    fs.writeFileSync(tempZip, buffer);
    if (tempManifest) {
      fs.writeFileSync(tempManifest, `${JSON.stringify(manifest, null, 2)}\n`);
    }
    fs.renameSync(tempZip, outputPath);
    if (tempManifest) fs.renameSync(tempManifest, manifestPath);
  } finally {
    fs.rmSync(tempZip, { force: true });
    if (tempManifest) fs.rmSync(tempManifest, { force: true });
  }
  return {
    zipPath: outputPath,
    outputPath,
    manifestPath,
    manifest,
    sha256: sha256(buffer),
    entries
  };
}

export function validatePayloadZip({
  rootDir,
  platform,
  hostPlatform = process.platform,
  requiredStagingPaths = REQUIRED_RELEASE_INPUTS[platform]
}) {
  if (!requiredStagingPaths)
    throw new Error(`Unsupported payload platform: ${platform}`);
  const { sourceDir, zipPath, manifestPath } = platformPaths(rootDir, platform);
  assertRequiredStagingInputs(sourceDir, requiredStagingPaths);
  assertStagedModWritesV5DataRoot(sourceDir);
  assertForbiddenStagingInputs(platform, sourceDir);
  if (!fs.statSync(zipPath, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(`Missing ${platform} release payload ZIP: ${zipPath}`);
  }
  if (!fs.statSync(manifestPath, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(
      `Missing ${platform} payload checksum manifest: ${manifestPath}`
    );
  }
  const zipBuffer = fs.readFileSync(zipPath);
  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  if (manifest.schemaVersion !== 1 || manifest.platform !== platform) {
    throw new Error(
      `Invalid payload checksum manifest for ${platform}: ${manifestPath}`
    );
  }
  if (manifest.zipSha256 !== sha256(zipBuffer)) {
    throw new Error(
      `${platform} payload ZIP checksum does not match its manifest`
    );
  }
  const files = listPayloadFiles(sourceDir, { platform, hostPlatform });
  assertMacosExecutableModes(platform, files);
  const entries = readZipEntries(zipBuffer);
  const mapping = validateZipEntrySet(
    entries,
    files.map((file) => file.path)
  );
  const manifestEntries = new Map(
    manifest.entries.map((entry) => [entry.path, entry])
  );
  validateZipEntrySet(
    manifest.entries.map((entry) => ({ name: entry.path, isDirectory: false })),
    files.map((file) => file.path)
  );

  for (const file of files) {
    const source = fs.readFileSync(file.absolutePath);
    const zipEntry = mapping.get(file.path);
    const checksumEntry = manifestEntries.get(file.path);
    if (
      !zipEntry ||
      !checksumEntry ||
      checksumEntry.size !== source.length ||
      checksumEntry.sha256 !== sha256(source) ||
      sha256(zipEntry.data) !== checksumEntry.sha256 ||
      checksumEntry.mode !== file.mode ||
      zipEntry.mode !== file.mode
    ) {
      throw new Error(
        `${platform} payload content or metadata drifted for ${file.path}`
      );
    }
  }
  return { zipPath, manifestPath, entries: mapping };
}

function main(args) {
  if (args[0] === 'pack') {
    const sourceIndex = args.indexOf('--source');
    const outputIndex = args.indexOf('--output');
    const manifestIndex = args.indexOf('--manifest-output');
    const platformIndex = args.indexOf('--platform');
    const sourceDir = sourceIndex < 0 ? undefined : args[sourceIndex + 1];
    const outputPath = outputIndex < 0 ? undefined : args[outputIndex + 1];
    const manifestPath =
      manifestIndex < 0 ? undefined : args[manifestIndex + 1];
    const platform =
      platformIndex < 0
        ? undefined
        : resolveBuildPlatform(args[platformIndex + 1]);
    if (
      !sourceDir ||
      !outputPath ||
      (manifestIndex >= 0 && (!manifestPath || !platform))
    ) {
      throw new Error(
        'Usage: payload-zip.mjs pack --source <directory> --output <zip> [--manifest-output <json> --platform <macos|windows>]'
      );
    }
    const result = writeDeterministicZip({
      sourceDir,
      outputPath,
      manifestPath,
      platform
    });
    console.log(`payload-zip: wrote ${result.outputPath}`);
    if (result.manifestPath) {
      console.log(`payload-zip: wrote ${result.manifestPath}`);
    }
    return;
  }
  if (args.length !== 2 || args[0] !== '--platform') {
    throw new Error(
      'Usage: payload-zip.mjs --platform <macos|windows> | pack --source <directory> --output <zip> [--manifest-output <json> --platform <macos|windows>]'
    );
  }
  const platform = resolveBuildPlatform(args[1]);
  if (!platform) throw new Error(`Unsupported payload platform: ${args[1]}`);
  const rootDir = path.resolve(import.meta.dirname, '..', '..');
  const result = preparePayloadZip({ rootDir, platform });
  console.log(`prepare:resources: wrote ${result.zipPath}`);
  console.log(`prepare:resources: wrote ${result.manifestPath}`);
}

if (import.meta.main) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
