import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import type { SpawnSyncReturns } from 'node:child_process';
import { loadGithubSecrets, localSource } from '../release/github-secrets.ts';
import type { GithubSecretItem } from '../release/github-secrets.ts';
import { readConfig, resolveSigning, root } from './local-config.ts';
import type { ParsedConfig } from './local-config.ts';

// `bun scripts/workspace.ts secrets check|push`: the GitHub side of
// release/github-secrets.json. check lists, by name only, what each scope
// holds on GitHub and what config.ini and keys/ hold locally; push copies
// every locally present item through `gh secret set` / `gh variable set`
// over stdin. Values never reach a command line, a file or the output.

const GH_SCOPE = /^(repository|[a-z][a-z0-9-]*)-(secret|variable)$/;

/** The `gh` addressing of one scope: secret or variable, and its environment. */
export interface GhScope {
  kind: string;
  env: string | null;
}

// `gh` arguments that address one scope: repository, or an environment.
export function ghScope(scope: string): GhScope {
  const match = GH_SCOPE.exec(scope);
  if (!match) throw new Error(`Unknown GitHub scope ${scope}`);
  const [, where, kind] = match;
  return { kind, env: where === 'repository' ? null : where };
}

interface RunGhOptions {
  input?: Buffer;
}

function runGh(
  gh: string,
  args: string[],
  { input }: RunGhOptions = {}
): SpawnSyncReturns<string> {
  const result = spawnSync(gh, args, {
    cwd: root,
    encoding: 'utf8',
    input,
    timeout: 60_000
  });
  if (result.error)
    throw new Error('Cannot start gh; install the GitHub CLI and log in');
  return result;
}

// The GitHub Environment or app a `gh` list or set call addresses.
interface GhTarget {
  env?: string | null;
  app?: string | null;
}

function listNames(
  gh: string,
  kind: string,
  { env = null, app = null }: GhTarget = {}
): Set<string> {
  const args = [kind, 'list', '--json', 'name'];
  if (env) args.push('--env', env);
  if (app) args.push('--app', app);
  const result = runGh(gh, args);
  if (result.status !== 0)
    throw new Error(
      `gh ${args.slice(0, 2).join(' ')}${env ? ` --env ${env}` : ''}${app ? ` --app ${app}` : ''} failed: ${result.stderr.trim()}`
    );
  const entries = JSON.parse(result.stdout) as { name: string }[];
  return new Set(entries.map((entry) => entry.name));
}

// The value an item copies, as bytes, or null when its local source is empty
// or absent. Reads happen here and nowhere else.
function localValue(
  item: GithubSecretItem,
  home: string,
  config: ParsedConfig,
  env: NodeJS.ProcessEnv
): Buffer | null {
  const { local } = item;
  if (local.section) {
    // assertItem also accepts a section without a key beside a keys/ file;
    // such an item reads the key "undefined", as before typing.
    const value =
      config.sections.get(local.section)?.values[local.key as string] ?? '';
    return value.trim() ? Buffer.from(value) : null;
  }
  let file;
  if (local.file === 'keys/AuthKey_<APPLE_API_KEY>.p8')
    file = resolveSigning(home, env).appleKeyPath;
  // An item with an empty section reaches here without a file and throws
  // in path.join, as before typing.
  else file = path.join(home, local.file as string);
  if (!file || !fs.existsSync(file) || !fs.statSync(file).isFile()) return null;
  const bytes = fs.readFileSync(file);
  if (bytes.length === 0) return null;
  return local.encoded === 'base64'
    ? Buffer.from(bytes.toString('base64'))
    : bytes;
}

function describeScope(scope: string): string {
  const { kind, env } = ghScope(scope);
  return `${env ? `${env} environment` : 'repository'} ${kind}`;
}

/** Options of inventory: the configuration home and how to reach GitHub. */
export interface InventoryOptions {
  home: string;
  env?: NodeJS.ProcessEnv;
  gh?: string;
  dependabot?: boolean;
  workspaceRoot?: string;
}

/** One managed name: where it lives on GitHub and whether it has a local value. */
export interface InventoryRow {
  item: GithubSecretItem;
  scope: string;
  onGithub: boolean;
  onDependabot: boolean | null;
  local: boolean;
  source: string;
}

/** What inventory returns: one row per item and the config it read. */
export interface Inventory {
  rows: InventoryRow[];
  config: ParsedConfig;
}

// One row per item: its scope, name, whether GitHub has it and whether the
// local source holds a value.
export function inventory({
  home,
  env = process.env,
  gh = 'gh',
  dependabot = false,
  workspaceRoot = root
}: InventoryOptions): Inventory {
  const table = loadGithubSecrets(workspaceRoot);
  const config = readConfig(home);
  const scopes = new Set(table.items.map((item) => item.scope));
  const remote = new Map<string, Set<string>>();
  for (const scope of scopes) {
    const { kind, env: environment } = ghScope(scope);
    remote.set(scope, listNames(gh, kind, { env: environment }));
  }
  if (dependabot)
    remote.set(
      'dependabot-secret',
      listNames(gh, 'secret', { app: 'dependabot' })
    );
  // Every scope and, with dependabot, the Dependabot list were read above.
  const names = (scope: string): Set<string> => {
    const listed = remote.get(scope);
    if (!listed) throw new Error(`No GitHub listing for ${scope}`);
    return listed;
  };
  const rows = table.items.map((item) => ({
    item,
    scope: describeScope(item.scope),
    onGithub: names(item.scope).has(item.name),
    onDependabot:
      dependabot && item.dependabot
        ? names('dependabot-secret').has(item.name)
        : null,
    local: localValue(item, home, config, env) !== null,
    source: localSource(item)
  }));
  return { rows, config };
}

interface FormatInventoryOptions {
  dependabot?: boolean;
}

export function formatInventory(
  { rows }: Pick<Inventory, 'rows'>,
  { dependabot = false }: FormatInventoryOptions = {}
): string {
  const width = Math.max(...rows.map((row) => row.item.name.length));
  const scopeWidth = Math.max(...rows.map((row) => row.scope.length));
  const lines = rows.map((row) => {
    const github = row.onGithub ? 'present' : 'MISSING';
    const local = row.local ? 'present' : 'MISSING';
    const extra =
      row.onDependabot === null
        ? ''
        : `, dependabot ${row.onDependabot ? 'present' : 'MISSING'}`;
    return `${row.item.name.padEnd(width)}  ${row.scope.padEnd(scopeWidth)}  GitHub ${github}${extra}  local ${local} (${row.source})`;
  });
  const missingRemote = rows.filter((row) => !row.onGithub).length;
  const missingLocal = rows.filter((row) => !row.local).length;
  lines.push(
    `${rows.length} managed names: ${missingRemote} missing on GitHub, ${missingLocal} missing locally.`
  );
  if (dependabot === false && rows.some((row) => row.item.dependabot))
    lines.push(
      'Dependabot secrets were not listed; pass --dependabot to include them.'
    );
  return lines.join('\n');
}

function setOne(
  gh: string,
  kind: string,
  name: string,
  bytes: Buffer,
  { env = null, app = null }: GhTarget
): void {
  const args = [kind, 'set', name];
  if (env) args.push('--env', env);
  if (app) args.push('--app', app);
  const result = runGh(gh, args, { input: bytes });
  if (result.status !== 0)
    throw new Error(`gh ${kind} set ${name} failed: ${result.stderr.trim()}`);
}

/** Options of push: inventory's, plus where progress lines go. */
export interface PushOptions extends InventoryOptions {
  log?: (line: string) => void;
}

/** How many names push set and how many had no local value. */
export interface PushResult {
  set: number;
  skipped: number;
}

// Copies every item with a local value; names the ones without one. With
// --dependabot, items marked for Dependabot are set there too.
export function push({
  home,
  env = process.env,
  gh = 'gh',
  dependabot = false,
  workspaceRoot = root,
  log = console.log
}: PushOptions): PushResult {
  const { rows, config } = inventory({
    home,
    env,
    gh,
    dependabot,
    workspaceRoot
  });
  const skipped: InventoryRow[] = [];
  for (const row of rows) {
    const bytes = localValue(row.item, home, config, env);
    if (bytes === null) {
      skipped.push(row);
      continue;
    }
    const { kind, env: environment } = ghScope(row.item.scope);
    setOne(gh, kind, row.item.name, bytes, { env: environment });
    log(`set ${row.scope} ${row.item.name}`);
    if (dependabot && row.item.dependabot) {
      setOne(gh, 'secret', row.item.name, bytes, { app: 'dependabot' });
      log(`set dependabot secret ${row.item.name}`);
    }
  }
  for (const row of skipped)
    log(
      `skipped ${row.scope} ${row.item.name}: no local value (${row.source})`
    );
  return { set: rows.length - skipped.length, skipped: skipped.length };
}
