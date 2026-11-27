import fs from 'node:fs';
import path from 'node:path';

// The workspace CLI, CI release-secrets.ts and local-config.ts share this table
// of names, scopes and signing staging filenames. It contains no secret values.

export type GithubSecretScope =
  | 'repository-secret'
  | 'repository-variable'
  | 'release-secret'
  | 'release-variable'
  | 'analyzer-secret'
  | 'steam-secret'
  | 'qqbot-secret';
/** The first release.yml stage that needs an item. */
export type GithubSecretReleaseStage = 'prepare' | 'release';
/** The release.yml platform whose signing needs an item. */
export type GithubSecretReleasePlatform = 'macos' | 'windows' | 'android';

export interface GithubSecretLocal {
  section?: string;
  key?: string;
  file?: string;
  encoded?: 'base64';
}

export interface GithubSecretRelease {
  stage: GithubSecretReleaseStage;
  platform?: GithubSecretReleasePlatform;
}

export interface GithubSecretItem {
  name: string;
  scope: GithubSecretScope;
  local: GithubSecretLocal;
  release?: GithubSecretRelease;
  dependabot?: boolean;
  purpose: string;
}

export interface GithubSecretsTable {
  items: GithubSecretItem[];
}

export interface GithubSecretsCliOptions {
  workspaceRoot?: string;
}

export const GITHUB_SECRETS_PATH = 'release/github-secrets.json';
export const SCOPES = Object.freeze([
  'repository-secret',
  'repository-variable',
  'release-secret',
  'release-variable',
  'analyzer-secret',
  'steam-secret',
  'qqbot-secret'
]);
export const RELEASE_STAGES = Object.freeze(['prepare', 'release']);
export const RELEASE_PLATFORMS = Object.freeze(['macos', 'windows', 'android']);

const NAME = /^[A-Z][A-Z0-9_]*$/;

function assertItem(item: GithubSecretItem): void {
  if (!NAME.test(item.name ?? ''))
    throw new Error(`github-secrets item has an invalid name ${item.name}`);
  if (!SCOPES.includes(item.scope))
    throw new Error(`${item.name}: scope must be one of ${SCOPES.join(', ')}`);
  const local = item.local ?? {};
  const config =
    typeof local.section === 'string' && typeof local.key === 'string';
  const file = typeof local.file === 'string' && local.file.startsWith('keys/');
  if (config === file)
    throw new Error(
      `${item.name}: local must name a config.toml section and key, or a keys/ file`
    );
  if (local.encoded !== undefined && local.encoded !== 'base64')
    throw new Error(`${item.name}: encoded must be base64 when set`);
  if (item.release !== undefined) {
    if (!RELEASE_STAGES.includes(item.release.stage))
      throw new Error(`${item.name}: release.stage must be prepare or release`);
    if (
      item.release.platform !== undefined &&
      !RELEASE_PLATFORMS.includes(item.release.platform)
    )
      throw new Error(
        `${item.name}: release.platform must be one of ${RELEASE_PLATFORMS.join(', ')}`
      );
  }
  if (typeof item.purpose !== 'string' || !item.purpose)
    throw new Error(`${item.name}: purpose is required`);
}

export function loadGithubSecrets(
  workspaceRoot: string = path.resolve(import.meta.dirname, '..')
): GithubSecretsTable {
  const file = path.join(workspaceRoot, GITHUB_SECRETS_PATH);
  const table = JSON.parse(fs.readFileSync(file, 'utf8')) as {
    items?: GithubSecretItem[];
  };
  if (!Array.isArray(table.items))
    throw new Error(`${GITHUB_SECRETS_PATH} needs an items array`);
  const seen = new Set<string>();
  for (const item of table.items) {
    assertItem(item);
    const key = `${item.scope}:${item.name}`;
    if (seen.has(key)) throw new Error(`${GITHUB_SECRETS_PATH} repeats ${key}`);
    seen.add(key);
  }
  return { items: table.items };
}

export function localSource(item: GithubSecretItem): string {
  const { local } = item;
  if (local.file)
    return local.encoded === 'base64'
      ? `base64 of ${local.file}`
      : `contents of ${local.file}`;
  return `config.toml [${local.section}] ${local.key}`;
}

export function releaseWorkflowNames(table: GithubSecretsTable): string[] {
  return table.items.filter((item) => item.release).map((item) => item.name);
}

export const SIGNING_STAGE_CONFIG_FILES: Readonly<Record<string, string>> =
  Object.freeze({
    'apple-signing-identity': 'APPLE_SIGNING_IDENTITY',
    'apple-api-issuer': 'APPLE_API_ISSUER',
    'apple-api-key': 'APPLE_API_KEY',
    'apple-api-key-path': 'APPLE_API_KEY_PATH',
    'android-keystore-password': 'ANDROID_KEYSTORE_PASSWORD',
    'android-key-alias': 'ANDROID_KEY_ALIAS'
  });

export interface StagedSigningEntry {
  name: string;
  file: string | ((env: NodeJS.ProcessEnv) => string);
  encoding?: 'base64';
}

// Signing files staged into BPP_SIGNING_SECRETS_DIR for bundle.ts (desktop) and
// release/android-release.ts (android). APPLE_API_KEY_PATH is inferred from
// AuthKey_<APPLE_API_KEY>.p8.
export function stagedSigningEntries(
  platform: GithubSecretReleasePlatform
): StagedSigningEntry[] {
  const entries: StagedSigningEntry[] = [
    { name: 'TAURI_SIGNING_PRIVATE_KEY', file: 'tauri-updater.key' }
  ];
  if (platform === 'macos') {
    entries.push(
      { name: 'APPLE_SIGNING_IDENTITY', file: 'apple-signing-identity' },
      { name: 'APPLE_API_ISSUER', file: 'apple-api-issuer' },
      { name: 'APPLE_API_KEY', file: 'apple-api-key' },
      {
        name: 'APPLE_API_KEY_P8',
        file: (env) => `AuthKey_${env.APPLE_API_KEY}.p8`
      }
    );
  } else if (platform === 'android') {
    entries.push(
      {
        name: 'ANDROID_KEYSTORE',
        file: 'android-release.p12',
        encoding: 'base64'
      },
      { name: 'ANDROID_KEYSTORE_PASSWORD', file: 'android-keystore-password' },
      { name: 'ANDROID_KEY_ALIAS', file: 'android-key-alias' }
    );
  }
  return entries;
}

export function releaseWorkflowSecrets(table: GithubSecretsTable): string[] {
  return table.items
    .filter((item) => item.release && item.scope.endsWith('-secret'))
    .map((item) => item.name);
}

export function releaseWorkflowVariables(table: GithubSecretsTable): string[] {
  return table.items
    .filter((item) => item.release && item.scope.endsWith('-variable'))
    .map((item) => item.name);
}

export function requiredForRelease(
  table: GithubSecretsTable,
  platform: string,
  stage: string
): string[] {
  if (!RELEASE_PLATFORMS.includes(platform))
    throw new Error(
      `platform must be one of ${RELEASE_PLATFORMS.join(', ')}, got '${platform}'`
    );
  if (!RELEASE_STAGES.includes(stage))
    throw new Error(`stage must be prepare or release, got '${stage}'`);
  return table.items
    .filter(
      (item) =>
        item.release &&
        (item.release.stage === 'prepare' || stage === 'release') &&
        (!item.release.platform || item.release.platform === platform)
    )
    .map((item) => item.name);
}
