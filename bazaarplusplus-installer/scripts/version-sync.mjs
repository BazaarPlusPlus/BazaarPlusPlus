import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function writeJson(filePath, value) {
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`);
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function readText(filePath) {
  return fs.readFileSync(filePath, 'utf8');
}

function writeText(filePath, value) {
  fs.writeFileSync(filePath, value);
}

function matchRequired(text, pattern, description) {
  const match = text.match(pattern);
  if (!match) {
    throw new Error(`Unable to find ${description}`);
  }
  return match;
}

function readPackageVersion(rootDir) {
  return readJson(path.join(rootDir, 'package.json')).version;
}

function tauriConfigPath(rootDir) {
  return path.join(rootDir, 'src-tauri', 'tauri.conf.json');
}

function cargoTomlPath(rootDir) {
  return path.join(rootDir, 'src-tauri', 'Cargo.toml');
}

function cargoLockPath(rootDir) {
  return path.join(rootDir, 'src-tauri', 'Cargo.lock');
}

function readTauriVersion(rootDir) {
  return readJson(tauriConfigPath(rootDir)).version;
}

function readCargoPackageName(rootDir) {
  const cargoToml = readText(cargoTomlPath(rootDir));
  return matchRequired(
    cargoToml,
    /^\[package\][\s\S]*?^name = "([^"]+)"$/m,
    'Cargo package name'
  )[1];
}

function readCargoVersion(rootDir) {
  const cargoToml = readText(cargoTomlPath(rootDir));
  return matchRequired(
    cargoToml,
    /^\[package\][\s\S]*?^version = "([^"]+)"$/m,
    'Cargo package version'
  )[1];
}

function readCargoLockVersion(rootDir, packageName) {
  const cargoLockFile = cargoLockPath(rootDir);
  if (!fs.existsSync(cargoLockFile)) {
    return null;
  }

  const cargoLock = readText(cargoLockFile);
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

function replaceRequired(text, pattern, replacement, description) {
  if (!pattern.test(text)) {
    throw new Error(`Unable to update ${description}`);
  }
  return text.replace(pattern, replacement);
}

function updateTauriVersion(rootDir, version) {
  const filePath = tauriConfigPath(rootDir);
  const tauriConfig = readJson(filePath);
  tauriConfig.version = version;
  writeJson(filePath, tauriConfig);
}

function updateCargoVersion(rootDir, version) {
  const filePath = cargoTomlPath(rootDir);
  const cargoToml = readText(filePath);
  const updatedCargoToml = replaceRequired(
    cargoToml,
    /^(\[package\][\s\S]*?^version = ")([^"]+)(")$/m,
    `$1${version}$3`,
    'Cargo.toml package version'
  );
  writeText(filePath, updatedCargoToml);
}

function updateCargoLockVersion(rootDir, packageName, version) {
  const filePath = cargoLockPath(rootDir);
  if (!fs.existsSync(filePath)) {
    return;
  }

  const cargoLock = readText(filePath);
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
  writeText(filePath, updatedCargoLock);
}

export function collectVersionSnapshot(rootDir) {
  const packageVersion = readPackageVersion(rootDir);
  const packageName = readCargoPackageName(rootDir);

  return {
    packageVersion,
    tauriVersion: readTauriVersion(rootDir),
    cargoVersion: readCargoVersion(rootDir),
    cargoLockVersion: readCargoLockVersion(rootDir, packageName)
  };
}

export function assertVersionsAreAligned(snapshot) {
  const mismatches = Object.entries(snapshot).filter(
    ([key, value]) =>
      key !== 'packageVersion' &&
      value !== null &&
      value !== snapshot.packageVersion
  );

  if (mismatches.length === 0) {
    return;
  }

  const details = mismatches
    .map(([key, value]) => `${key}=${value}`)
    .join(', ');
  throw new Error(
    `Version mismatch: packageVersion=${snapshot.packageVersion}, ${details}`
  );
}

export function synchronizeVersions(rootDir) {
  const packageVersion = readPackageVersion(rootDir);
  const packageName = readCargoPackageName(rootDir);

  updateTauriVersion(rootDir, packageVersion);
  updateCargoVersion(rootDir, packageVersion);
  updateCargoLockVersion(rootDir, packageName, packageVersion);

  return collectVersionSnapshot(rootDir);
}

const invokedAsScript =
  process.argv[1] && path.resolve(process.argv[1]) === import.meta.filename;

if (invokedAsScript) {
  const snapshot = synchronizeVersions(process.cwd());
  console.log(`version-sync: aligned to ${snapshot.packageVersion}`);
}
