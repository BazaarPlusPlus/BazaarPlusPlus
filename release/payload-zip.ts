import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { parseArgs } from 'node:util';
import zlib from 'node:zlib';
import { resolveBuildPlatform } from './release-platforms.ts';
import type { BuildPlatform, TauriBuildPlatform } from './release-platforms.ts';
import { readProductVersion } from './product.ts';
import type { ProductVersion } from './product.ts';
import {
  INSTALLER_HISTORY_DATABASE_COMPATIBILITY_PATH,
  assertHistoryDatabaseCompatibility
} from './history-database.ts';
import {
  assertShippedPayloadPaths,
  requiredPayloadPaths
} from './payload-inventory.ts';

/** One staged Payload file, as listPayloadFiles returns it. */
export interface PayloadFile {
  path: string;
  absolutePath: string;
  mode: number;
}

/** The options of listPayloadFiles. */
export interface ListPayloadFilesOptions {
  platform?: BuildPlatform | null;
  hostPlatform?: NodeJS.Platform;
}

/** One entry buildZipBuffer writes. An entry without mode gets no mode bits. */
export interface ZipEntryInput {
  name: string;
  data: Buffer | Uint8Array;
  mode?: number;
  isDirectory?: boolean;
}

/** One entry readZipEntries reads back. */
export interface ZipEntry {
  name: string;
  isDirectory: boolean;
  mode: number;
  data: Buffer;
}

/** The entry fields validateZipEntrySet checks. */
export interface ZipEntryName {
  name: string;
  isDirectory?: boolean;
}

/** One file of the Payload checksum manifest. */
export interface PayloadZipManifestEntry {
  path: string;
  size: number;
  sha256: string;
  mode: number;
}

/** The Payload checksum manifest, BepInEx.zip.manifest.json. */
export interface PayloadZipManifest {
  schemaVersion: 1;
  platform: BuildPlatform;
  zipSha256: string;
  entries: PayloadZipManifestEntry[];
}

/** The options of preparePayloadZip and validatePayloadZip. */
export interface PayloadZipOptions {
  rootDir: string;
  platform: TauriBuildPlatform;
  productVersion?: ProductVersion;
  hostPlatform?: NodeJS.Platform;
  requiredStagingPaths?: readonly string[];
}

/** The options of writeDeterministicZip. */
export interface WriteDeterministicZipOptions {
  sourceDir: string;
  outputPath: string;
  manifestPath?: string;
  platform?: BuildPlatform | null;
  hostPlatform?: NodeJS.Platform;
}

/** What writeDeterministicZip wrote. */
export interface DeterministicZip {
  zipPath: string;
  manifestPath: string | undefined;
  manifest: PayloadZipManifest | undefined;
  sha256: string;
  entries: { name: string; data: Buffer; mode: number }[];
}

/** What validatePayloadZip checked. */
export interface ValidatedPayloadZip {
  zipPath: string;
  manifestPath: string;
  entries: Map<string, ZipEntry>;
}

export const REQUIRED_RELEASE_INPUTS: Readonly<
  Record<TauriBuildPlatform, readonly string[]>
> = Object.freeze({
  macos: Object.freeze(requiredPayloadPaths('macos')),
  windows: Object.freeze(requiredPayloadPaths('windows'))
});

const osArtifactNames = new Set(['.DS_Store', 'Thumbs.db', 'desktop.ini']);
const macosExecutablePaths = new Set([
  'TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox'
]);
const fixedDosDate = (1 << 5) | 1;
const fixedDosTime = 0;

function sha256(buffer: string | NodeJS.ArrayBufferView): string {
  return crypto.createHash('sha256').update(buffer).digest('hex');
}

function platformPaths(rootDir: string, platform: TauriBuildPlatform) {
  const resources = path.join(rootDir, 'src-tauri', 'resources');
  const outputDir = path.join(resources, 'BepInExSource', platform);
  return {
    sourceDir: path.join(resources, 'SourceForBuild', platform),
    zipPath: path.join(outputDir, 'BepInEx.zip'),
    manifestPath: path.join(outputDir, 'BepInEx.zip.manifest.json')
  };
}

interface PayloadFileModeFacts {
  hostPlatform: NodeJS.Platform;
  platform: BuildPlatform | null | undefined;
  relativePath: string;
  sourceMode: number;
}

function payloadFileMode({
  hostPlatform,
  platform,
  relativePath,
  sourceMode
}: PayloadFileModeFacts): number {
  if (hostPlatform !== 'win32') return sourceMode & 0o777;
  return platform === 'macos' && macosExecutablePaths.has(relativePath)
    ? 0o755
    : 0o644;
}

export function listPayloadFiles(
  sourceDir: string,
  { platform, hostPlatform = process.platform }: ListPayloadFilesOptions = {}
): PayloadFile[] {
  const files: PayloadFile[] = [];
  const walk = (directory: string) => {
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

function assertRequiredStagingInputs(
  sourceDir: string,
  requiredStagingPaths: readonly string[]
) {
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
    ? '\nRun bun release.ts prepare --platform <macos|windows> from the workspace root to prepare the selected platform.'
    : '';
  throw new Error(
    `Missing release staging inputs under SourceForBuild; provide every external/private file before preparing resources:\n${details}${publishGuidance}`
  );
}

// The staged mod must be the product version being packaged. Callers pass a
// VERSION that already passed assertProductVersion, so equality also rejects
// every unparseable or older staged version.
function assertStagedModVersion(
  sourceDir: string,
  productVersion: ProductVersion
) {
  const versionPath = path.join(
    sourceDir,
    'BepInEx',
    'plugins',
    'BazaarPlusPlus.version'
  );
  const content = fs.readFileSync(versionPath, 'utf8');
  if (content.trim() !== `${productVersion}.prod`) {
    throw new Error(
      `Payload product version mismatch: expected ${productVersion}.prod, found ${content.trim()}. Run bun release.ts prepare for this platform.`
    );
  }
}

function assertStagedHistoryDatabaseCompatibility(
  rootDir: string,
  sourceDir: string
) {
  assertHistoryDatabaseCompatibility({
    contractPath: path.join(
      sourceDir,
      'BepInEx',
      'plugins',
      'BazaarPlusPlus.history-database.json'
    ),
    compatibilityPath: path.join(
      rootDir,
      INSTALLER_HISTORY_DATABASE_COMPATIBILITY_PATH
    )
  });
}

function assertForbiddenStagingInputs(
  platform: BuildPlatform,
  sourceDir: string
) {
  const paths: string[] = [];
  const walk = (directory: string, prefix = '') => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const relativePath = prefix ? `${prefix}/${entry.name}` : entry.name;
      paths.push(relativePath);
      if (entry.isDirectory())
        walk(path.join(directory, entry.name), relativePath);
    }
  };
  walk(sourceDir);
  // Structural parent directories are not ownership claims; empty retired and
  // unknown leaf directories still fail the same inventory gate as files.
  const leaves = paths.filter(
    (candidate) => !paths.some((other) => other.startsWith(`${candidate}/`))
  );
  assertShippedPayloadPaths(leaves, platform);
}

function assertMacosExecutableModes(
  platform: BuildPlatform,
  files: readonly PayloadFile[]
) {
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

export function buildZipBuffer(inputEntries: readonly ZipEntryInput[]): Buffer {
  const entries = [...inputEntries].sort((left, right) =>
    left.name.localeCompare(right.name)
  );
  const localParts: Buffer[] = [];
  const centralParts: Buffer[] = [];
  let localOffset = 0;

  for (const entry of entries) {
    const name = Buffer.from(entry.name, 'utf8');
    const data = Buffer.from(entry.data);
    const isDirectory = entry.isDirectory ?? entry.name.endsWith('/');
    const method = isDirectory ? 0 : 8;
    const compressed = isDirectory
      ? Buffer.alloc(0)
      : zlib.deflateRawSync(data, { level: 9 });
    const checksum = zlib.crc32(data);
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
    central.writeUInt32LE(((fileType | (entry.mode ?? 0)) << 16) >>> 0, 38);
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

function findEndOfCentralDirectory(buffer: Buffer): number {
  for (let offset = buffer.length - 22; offset >= 0; offset -= 1) {
    if (buffer.readUInt32LE(offset) === 0x06054b50) return offset;
  }
  throw new Error('ZIP end-of-central-directory record not found');
}

export function readZipEntries(buffer: Buffer): ZipEntry[] {
  const endOffset = findEndOfCentralDirectory(buffer);
  const centralSize = buffer.readUInt32LE(endOffset + 12);
  const centralOffset = buffer.readUInt32LE(endOffset + 16);
  const centralEnd = centralOffset + centralSize;
  const entries: ZipEntry[] = [];
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
    let data: Buffer;
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

function normalizedEntryName(entry: ZipEntryName): string {
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
  if (osArtifactNames.has(normalized.split('/').at(-1) ?? '')) {
    throw new Error(`ZIP entry is an OS artifact: ${entry.name}`);
  }
  return normalized;
}

function setDifference(
  left: ReadonlySet<string>,
  right: ReadonlySet<string>
): string[] {
  return [...left].filter((value) => !right.has(value)).sort();
}

export function validateZipEntrySet<T extends ZipEntryName>(
  entries: readonly T[],
  expectedPaths: readonly string[]
): Map<string, T> {
  const files: { normalized: string; entry: T }[] = [];
  const seen = new Map<string, T>();
  for (const entry of entries) {
    const normalized = normalizedEntryName(entry);
    if (entry.isDirectory) continue;
    const previous = seen.get(normalized);
    if (previous) {
      throw new Error(
        `ZIP contains duplicate normalized path '${normalized}' from '${previous.name}' and '${entry.name}'`
      );
    }
    seen.set(normalized, entry);
    files.push({ normalized, entry });
  }

  const expected = new Set(expectedPaths);
  const actual = new Set(files.map(({ normalized }) => normalized));

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
  return new Map(
    files.map(({ normalized, entry }): [string, T] => [normalized, entry])
  );
}

export function preparePayloadZip({
  rootDir,
  platform,
  productVersion = readProductVersion(path.dirname(rootDir)),
  hostPlatform = process.platform,
  requiredStagingPaths = REQUIRED_RELEASE_INPUTS[platform]
}: PayloadZipOptions): DeterministicZip {
  if (!requiredStagingPaths)
    throw new Error(`Unsupported payload platform: ${platform}`);
  const { sourceDir, zipPath, manifestPath } = platformPaths(rootDir, platform);
  assertRequiredStagingInputs(sourceDir, requiredStagingPaths);
  assertStagedModVersion(sourceDir, productVersion);
  assertStagedHistoryDatabaseCompatibility(rootDir, sourceDir);
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
}: WriteDeterministicZipOptions): DeterministicZip {
  if (platform) assertForbiddenStagingInputs(platform, sourceDir);
  const files = listPayloadFiles(sourceDir, { platform, hostPlatform });
  if (platform) assertMacosExecutableModes(platform, files);
  const entries = files.map((file) => ({
    name: file.path,
    data: fs.readFileSync(file.absolutePath),
    mode: file.mode
  }));
  const buffer = buildZipBuffer(entries);
  // A manifest without a platform is refused below, before anything is written.
  const manifest: PayloadZipManifest | undefined =
    manifestPath && platform
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
    if (tempManifest && manifestPath) fs.renameSync(tempManifest, manifestPath);
  } finally {
    fs.rmSync(tempZip, { force: true });
    if (tempManifest) fs.rmSync(tempManifest, { force: true });
  }
  return {
    zipPath: outputPath,
    manifestPath,
    manifest,
    sha256: sha256(buffer),
    entries
  };
}

export function validatePayloadZip({
  rootDir,
  platform,
  productVersion = readProductVersion(path.dirname(rootDir)),
  hostPlatform = process.platform,
  requiredStagingPaths = REQUIRED_RELEASE_INPUTS[platform]
}: PayloadZipOptions): ValidatedPayloadZip {
  if (!requiredStagingPaths)
    throw new Error(`Unsupported payload platform: ${platform}`);
  const { sourceDir, zipPath, manifestPath } = platformPaths(rootDir, platform);
  assertRequiredStagingInputs(sourceDir, requiredStagingPaths);
  assertStagedModVersion(sourceDir, productVersion);
  assertStagedHistoryDatabaseCompatibility(rootDir, sourceDir);
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
  const manifest = JSON.parse(
    fs.readFileSync(manifestPath, 'utf8')
  ) as PayloadZipManifest;
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
    manifest.entries.map((entry): [string, PayloadZipManifestEntry] => [
      entry.path,
      entry
    ])
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

// `pack` is internal to the locked product build (bazaarplusplus-installer
// scripts/bundle.sh); Payloads are prepared with bun release.ts prepare.
const PACK_USAGE =
  'Usage: payload-zip.ts pack --source <directory> --output <zip> [--manifest-output <json> --platform <macos|windows>]';

async function main(args: readonly string[]): Promise<void> {
  if (args[0] !== 'pack') throw new Error(PACK_USAGE);
  const { assertBuildOwner } = await import('./payload.ts');
  assertBuildOwner(
    path.resolve(import.meta.dirname, '..', 'bazaarplusplus-installer')
  );
  const { values } = parseArgs({
    args: args.slice(1),
    strict: true,
    options: {
      source: { type: 'string' },
      output: { type: 'string' },
      'manifest-output': { type: 'string' },
      platform: { type: 'string' }
    }
  });
  const sourceDir = values.source;
  const outputPath = values.output;
  const manifestPath = values['manifest-output'];
  const platform =
    values.platform === undefined
      ? undefined
      : resolveBuildPlatform(values.platform);
  if (
    !sourceDir ||
    !outputPath ||
    (manifestPath !== undefined && (!manifestPath || !platform))
  )
    throw new Error(PACK_USAGE);
  const result = writeDeterministicZip({
    sourceDir,
    outputPath,
    manifestPath,
    platform
  });
  console.log(`payload-zip: wrote ${result.zipPath}`);
  if (result.manifestPath) {
    console.log(`payload-zip: wrote ${result.manifestPath}`);
  }
}

if (import.meta.main) {
  main(process.argv.slice(2)).catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
