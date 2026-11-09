import fs from 'node:fs';
import path from 'node:path';

// Reads release/github-secrets.json, the one owner of every GitHub secret and
// variable the workflows consume, for `bun scripts/workspace.ts secrets` and
// .github/scripts/release-secrets.sh (through the CLI at the bottom). Nothing
// here reads a value; the table only names scopes and local sources.

/** The GitHub scope a secret or variable is set in. */
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

/** Where `secrets push` reads an item's value: a config.toml key or a keys/ file. */
export interface GithubSecretLocal {
  section?: string;
  key?: string;
  file?: string;
  encoded?: 'base64';
}

/** The release.yml stage, and optionally the platform, that needs an item. */
export interface GithubSecretRelease {
  stage: GithubSecretReleaseStage;
  platform?: GithubSecretReleasePlatform;
}

/** One item of release/github-secrets.json. */
export interface GithubSecretItem {
  name: string;
  scope: GithubSecretScope;
  local: GithubSecretLocal;
  release?: GithubSecretRelease;
  dependabot?: boolean;
  purpose: string;
}

/** release/github-secrets.json after validation. */
export interface GithubSecretsTable {
  items: GithubSecretItem[];
}

/** The options of cliMain. */
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
  // assertItem checks each item against the shape its type claims.
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

// Where `secrets push` reads the value, as release-secrets.sh prints it.
export function localSource(item: GithubSecretItem): string {
  const { local } = item;
  if (local.file)
    return local.encoded === 'base64'
      ? `base64 of ${local.file}`
      : `contents of ${local.file}`;
  return `config.toml [${local.section}] ${local.key}`;
}

// Names release.yml reads, in table order.
export function releaseWorkflowNames(table: GithubSecretsTable): string[] {
  return table.items.filter((item) => item.release).map((item) => item.name);
}

// Names one release.yml run needs: every prepare item, plus the signing items
// of the platform for a release stage.
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

const usage = `GitHub secrets table (release/github-secrets.json):
  bun release/github-secrets.ts names                      names release.yml reads
  bun release/github-secrets.ts required <macos|windows|android> <prepare|release>
  bun release/github-secrets.ts source NAME                where the value comes from
  bun release/github-secrets.ts secrets                    names release.yml reads as secrets
  bun release/github-secrets.ts variables                  names release.yml reads as variables`;

export function cliMain(
  argv: readonly string[],
  { workspaceRoot }: GithubSecretsCliOptions = {}
): number {
  const [verb, ...rest] = argv;
  try {
    const table = loadGithubSecrets(workspaceRoot);
    const print = (lines: readonly string[]) => {
      if (lines.length) process.stdout.write(`${lines.join('\n')}\n`);
    };
    switch (verb) {
      case 'names':
        print(releaseWorkflowNames(table));
        return 0;
      case 'secrets':
      case 'variables':
        print(
          table.items
            .filter(
              (item) =>
                item.release &&
                item.scope.endsWith(
                  verb === 'secrets' ? '-secret' : '-variable'
                )
            )
            .map((item) => item.name)
        );
        return 0;
      case 'required':
        if (rest.length !== 2) throw new Error(usage);
        print(requiredForRelease(table, rest[0], rest[1]));
        return 0;
      case 'source': {
        if (rest.length !== 1) throw new Error(usage);
        const item = table.items.find((entry) => entry.name === rest[0]);
        if (!item)
          throw new Error(`${rest[0]} is not in ${GITHUB_SECRETS_PATH}`);
        print([localSource(item)]);
        return 0;
      }
      default:
        throw new Error(usage);
    }
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`);
    return 2;
  }
}

if (import.meta.main) {
  process.exitCode = cliMain(process.argv.slice(2));
}
