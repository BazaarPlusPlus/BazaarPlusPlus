import fs from 'node:fs';
import path from 'node:path';
import { readProductVersion } from './product.ts';
import type { ProductVersion } from './product.ts';

// The fields of the installer's package.json, package-lock.json and
// tauri.conf.json this module reads or projects.
interface VersionedJson {
  version?: string;
}
interface PackageLock extends VersionedJson {
  packages?: Record<string, VersionedJson | undefined>;
}

/** Every version the installer's toolchain files state, beside VERSION. */
export interface VersionSnapshot {
  productVersion: ProductVersion;
  packageVersion: string | undefined;
  packageLockVersion: string | null;
  packageLockRootVersion: string | null;
  tauriVersion: string | undefined;
  cargoVersion: string;
  cargoLockVersion: string;
}

interface CargoPackage {
  name: string;
  version: string;
}

function readJson<T>(filePath: string): T {
  return JSON.parse(fs.readFileSync(filePath, 'utf8')) as T;
}

function writeJson(filePath: string, value: unknown): void {
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`);
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function matchRequired(
  text: string,
  pattern: RegExp,
  description: string
): RegExpMatchArray {
  const match = text.match(pattern);
  if (!match) {
    throw new Error(`Unable to find ${description}`);
  }
  return match;
}

function readPackageVersion(rootDir: string): string | undefined {
  return readJson<VersionedJson>(path.join(rootDir, 'package.json')).version;
}

function packageLockPath(rootDir: string): string {
  return path.join(rootDir, 'package-lock.json');
}

function readPackageLockVersions(
  rootDir: string
): Pick<VersionSnapshot, 'packageLockVersion' | 'packageLockRootVersion'> {
  const packageLock = readJson<PackageLock>(packageLockPath(rootDir));
  return {
    packageLockVersion: packageLock.version ?? null,
    packageLockRootVersion: packageLock.packages?.['']?.version ?? null
  };
}

function tauriConfigPath(rootDir: string): string {
  return path.join(rootDir, 'src-tauri', 'tauri.conf.json');
}

function cargoTomlPath(rootDir: string): string {
  return path.join(rootDir, 'src-tauri', 'Cargo.toml');
}

function cargoLockPath(rootDir: string): string {
  return path.join(rootDir, 'src-tauri', 'Cargo.lock');
}

function readTauriVersion(rootDir: string): string | undefined {
  return readJson<VersionedJson>(tauriConfigPath(rootDir)).version;
}

function readCargoPackage(rootDir: string): CargoPackage {
  const cargoToml = fs.readFileSync(cargoTomlPath(rootDir), 'utf8');
  return {
    name: matchRequired(
      cargoToml,
      /^\[package\][\s\S]*?^name = "([^"]+)"$/m,
      'Cargo package name'
    )[1],
    version: matchRequired(
      cargoToml,
      /^\[package\][\s\S]*?^version = "([^"]+)"$/m,
      'Cargo package version'
    )[1]
  };
}

function readCargoLockVersion(rootDir: string, packageName: string): string {
  const cargoLock = fs.readFileSync(cargoLockPath(rootDir), 'utf8');
  const pattern = new RegExp(
    String.raw`\[\[package\]\]\r?\nname = "${escapeRegExp(packageName)}"\r?\nversion = "([^"]+)"`,
    'm'
  );
  const match = cargoLock.match(pattern);
  if (!match) {
    throw new Error(`Unable to find ${packageName} version in Cargo.lock`);
  }
  return match[1];
}

function replaceRequired(
  text: string,
  pattern: RegExp,
  replacement: string,
  description: string
): string {
  if (!pattern.test(text)) {
    throw new Error(`Unable to update ${description}`);
  }
  return text.replace(pattern, replacement);
}

function updateTauriVersion(rootDir: string, version: ProductVersion): void {
  const filePath = tauriConfigPath(rootDir);
  const tauriConfig = fs.readFileSync(filePath, 'utf8');
  const updatedTauriConfig = replaceRequired(
    tauriConfig,
    /^(\s*"version"\s*:\s*")([^"]+)(",?\s*)$/m,
    `$1${version}$3`,
    'Tauri config version'
  );
  fs.writeFileSync(filePath, updatedTauriConfig);
}

function updateCargoVersion(rootDir: string, version: ProductVersion): void {
  const filePath = cargoTomlPath(rootDir);
  const cargoToml = fs.readFileSync(filePath, 'utf8');
  const updatedCargoToml = replaceRequired(
    cargoToml,
    /^(\[package\][\s\S]*?^version = ")([^"]+)(")$/m,
    `$1${version}$3`,
    'Cargo.toml package version'
  );
  fs.writeFileSync(filePath, updatedCargoToml);
}

function updatePackageLockVersion(
  rootDir: string,
  version: ProductVersion
): void {
  const filePath = packageLockPath(rootDir);
  const packageLock = readJson<PackageLock>(filePath);
  packageLock.version = version;
  const rootEntry = packageLock.packages?.[''];
  if (rootEntry) {
    rootEntry.version = version;
  }
  writeJson(filePath, packageLock);
}

function updateCargoLockVersion(
  rootDir: string,
  packageName: string,
  version: ProductVersion
): void {
  const filePath = cargoLockPath(rootDir);
  const cargoLock = fs.readFileSync(filePath, 'utf8');
  const pattern = new RegExp(
    String.raw`(\[\[package\]\]\r?\nname = "${escapeRegExp(packageName)}"\r?\nversion = ")([^"]+)(")`,
    'm'
  );
  const updatedCargoLock = replaceRequired(
    cargoLock,
    pattern,
    `$1${version}$3`,
    'Cargo.lock root package version'
  );
  fs.writeFileSync(filePath, updatedCargoLock);
}

export function collectVersionSnapshot(rootDir: string): VersionSnapshot {
  const packageVersion = readPackageVersion(rootDir);
  const cargoPackage = readCargoPackage(rootDir);
  const packageLockVersions = readPackageLockVersions(rootDir);

  return {
    productVersion: readProductVersion(path.dirname(rootDir)),
    packageVersion,
    ...packageLockVersions,
    tauriVersion: readTauriVersion(rootDir),
    cargoVersion: cargoPackage.version,
    cargoLockVersion: readCargoLockVersion(rootDir, cargoPackage.name)
  };
}

export function assertVersionsAreAligned(snapshot: VersionSnapshot): void {
  const expected = snapshot.productVersion;
  if (!expected) throw new Error('Missing product VERSION in version snapshot');
  const mismatches = Object.entries(snapshot).filter(
    ([key, value]) => key !== 'productVersion' && value !== expected
  );

  if (mismatches.length === 0) {
    return;
  }

  const details = mismatches
    .map(([key, value]) => `${key}=${value}`)
    .join(', ');
  throw new Error(`Version mismatch: productVersion=${expected}, ${details}`);
}

export function synchronizeVersions(rootDir: string): VersionSnapshot {
  const packageVersion = readProductVersion(path.dirname(rootDir));
  const packageName = readCargoPackage(rootDir).name;

  const packagePath = path.join(rootDir, 'package.json');
  const packageJson = readJson<VersionedJson>(packagePath);
  packageJson.version = packageVersion;
  writeJson(packagePath, packageJson);

  updatePackageLockVersion(rootDir, packageVersion);
  updateTauriVersion(rootDir, packageVersion);
  updateCargoVersion(rootDir, packageVersion);
  updateCargoLockVersion(rootDir, packageName, packageVersion);

  return collectVersionSnapshot(rootDir);
}
