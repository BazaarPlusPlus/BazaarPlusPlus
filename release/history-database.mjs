import fs from 'node:fs';
import path from 'node:path';

// The mod owns the local history database schema; the installer declares which
// `user_version` shapes its History reads understand. Both files change in the
// same pull request, and a data repair changes neither
// (bazaarplusplus-mod/docs/adr/0011-schema-version-names-column-shape.md).
export const MOD_HISTORY_DATABASE_CONTRACT_PATH =
  'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json';
export const INSTALLER_HISTORY_DATABASE_COMPATIBILITY_PATH =
  'src-tauri/history-database-compatibility.json';

function readJson(file, label) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    throw new Error(
      `Cannot read ${label} at ${file}: ${error instanceof Error ? error.message : String(error)}`,
      { cause: error }
    );
  }
}

function isPositiveInteger(value) {
  return Number.isSafeInteger(value) && value > 0;
}

export function readHistoryDatabaseCompatibility(compatibilityPath) {
  const compatibility = readJson(
    compatibilityPath,
    'installer history database compatibility contract'
  );
  const versions = compatibility.supportedUserVersions;
  if (
    compatibility.formatVersion !== 1 ||
    !Array.isArray(versions) ||
    versions.length === 0 ||
    versions.some(
      (version, index) =>
        !isPositiveInteger(version) ||
        (index > 0 && version <= versions[index - 1])
    )
  ) {
    throw new Error(
      `Invalid installer history database compatibility contract: ${compatibilityPath}`
    );
  }
  return versions;
}

export function readHistoryDatabaseContract(contractPath) {
  const contract = readJson(
    contractPath,
    'BazaarPlusPlus history database contract'
  );
  if (
    contract.formatVersion !== 1 ||
    !isPositiveInteger(contract.historyDatabaseUserVersion)
  ) {
    throw new Error(
      `Invalid BazaarPlusPlus history database contract: ${contractPath}`
    );
  }
  return contract.historyDatabaseUserVersion;
}

// The installer ships with the mod, so the mod's schema must be the newest one
// the installer reads: older versions stay listed for databases not yet opened
// by the current mod, and no listed version may be ahead of the mod.
export function assertHistoryDatabaseCompatibility({
  contractPath,
  compatibilityPath
}) {
  const userVersion = readHistoryDatabaseContract(contractPath);
  const supported = readHistoryDatabaseCompatibility(compatibilityPath);
  if (supported.at(-1) !== userVersion) {
    throw new Error(
      `BazaarPlusPlus database schema ${userVersion} (${contractPath}) must be the newest version the installer supports; the installer supports ${supported.join(',')} (${compatibilityPath}). Change both in one pull request; for a staged Payload, re-run just release::prepare <platform> from this revision.`
    );
  }
  return userVersion;
}

export function assertWorkspaceHistoryDatabaseCompatibility(workspaceRoot) {
  return assertHistoryDatabaseCompatibility({
    contractPath: path.join(workspaceRoot, MOD_HISTORY_DATABASE_CONTRACT_PATH),
    compatibilityPath: path.join(
      workspaceRoot,
      'bazaarplusplus-installer',
      INSTALLER_HISTORY_DATABASE_COMPATIBILITY_PATH
    )
  });
}
