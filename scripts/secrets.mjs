import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { loadGithubSecrets, localSource } from '../release/github-secrets.mjs';
import { readConfig, resolveSigning, root } from './local-config.mjs';

// `node scripts/workspace.mjs secrets check|push`: the GitHub side of
// release/github-secrets.json. check lists, by name only, what each scope
// holds on GitHub and what config.ini and keys/ hold locally; push copies
// every locally present item through `gh secret set` / `gh variable set`
// over stdin. Values never reach a command line, a file or the output.

const GH_SCOPE = /^(repository|[a-z][a-z0-9-]*)-(secret|variable)$/;

// `gh` arguments that address one scope: repository, or an environment.
export function ghScope(scope) {
  const match = GH_SCOPE.exec(scope);
  if (!match) throw new Error(`Unknown GitHub scope ${scope}`);
  const [, where, kind] = match;
  return { kind, env: where === 'repository' ? null : where };
}

function runGh(gh, args, { input } = {}) {
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

function listNames(gh, kind, { env = null, app = null } = {}) {
  const args = [kind, 'list', '--json', 'name'];
  if (env) args.push('--env', env);
  if (app) args.push('--app', app);
  const result = runGh(gh, args);
  if (result.status !== 0)
    throw new Error(
      `gh ${args.slice(0, 2).join(' ')}${env ? ` --env ${env}` : ''}${app ? ` --app ${app}` : ''} failed: ${result.stderr.trim()}`
    );
  return new Set(JSON.parse(result.stdout).map((entry) => entry.name));
}

// The value an item copies, as bytes, or null when its local source is empty
// or absent. Reads happen here and nowhere else.
function localValue(item, home, config, env) {
  const { local } = item;
  if (local.section) {
    const value = config.sections.get(local.section)?.values[local.key] ?? '';
    return value.trim() ? Buffer.from(value) : null;
  }
  let file;
  if (local.file === 'keys/AuthKey_<APPLE_API_KEY>.p8')
    file = resolveSigning(home, env).appleKeyPath;
  else file = path.join(home, local.file);
  if (!file || !fs.existsSync(file) || !fs.statSync(file).isFile()) return null;
  const bytes = fs.readFileSync(file);
  if (bytes.length === 0) return null;
  return local.encoded === 'base64'
    ? Buffer.from(bytes.toString('base64'))
    : bytes;
}

function describeScope(scope) {
  const { kind, env } = ghScope(scope);
  return `${env ? `${env} environment` : 'repository'} ${kind}`;
}

// One row per item: its scope, name, whether GitHub has it and whether the
// local source holds a value; then the retired names GitHub still holds.
export function inventory({
  home,
  env = process.env,
  gh = 'gh',
  dependabot = false,
  workspaceRoot = root
}) {
  const table = loadGithubSecrets(workspaceRoot);
  const config = readConfig(home);
  const scopes = new Set([
    ...table.items.map((item) => item.scope),
    ...table.retired.map((entry) => entry.scope)
  ]);
  const remote = new Map();
  for (const scope of scopes) {
    const { kind, env: environment } = ghScope(scope);
    remote.set(scope, listNames(gh, kind, { env: environment }));
  }
  if (dependabot)
    remote.set(
      'dependabot-secret',
      listNames(gh, 'secret', { app: 'dependabot' })
    );
  const rows = table.items.map((item) => ({
    item,
    scope: describeScope(item.scope),
    onGithub: remote.get(item.scope).has(item.name),
    onDependabot:
      dependabot && item.dependabot
        ? remote.get('dependabot-secret').has(item.name)
        : null,
    local: localValue(item, home, config, env) !== null,
    source: localSource(item)
  }));
  const stale = table.retired
    .filter((entry) => remote.get(entry.scope).has(entry.name))
    .map((entry) => ({
      ...entry,
      scope: describeScope(entry.scope),
      raw: entry.scope
    }));
  return { rows, stale, config };
}

export function formatInventory({ rows, stale }, { dependabot = false } = {}) {
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
  if (stale.length)
    lines.push(
      `Stale on GitHub (secrets push --prune removes them): ${stale.map((entry) => `${entry.scope} ${entry.name}`).join('; ')}`
    );
  if (dependabot === false && rows.some((row) => row.item.dependabot))
    lines.push(
      'Dependabot secrets were not listed; pass --dependabot to include them.'
    );
  return lines.join('\n');
}

function setOne(gh, kind, name, bytes, { env = null, app = null }) {
  const args = [kind, 'set', name];
  if (env) args.push('--env', env);
  if (app) args.push('--app', app);
  const result = runGh(gh, args, { input: bytes });
  if (result.status !== 0)
    throw new Error(`gh ${kind} set ${name} failed: ${result.stderr.trim()}`);
}

function deleteOne(gh, kind, name, { env = null }) {
  const args = [kind, 'delete', name];
  if (env) args.push('--env', env);
  const result = runGh(gh, args);
  if (result.status !== 0)
    throw new Error(
      `gh ${kind} delete ${name} failed: ${result.stderr.trim()}`
    );
}

// Copies every item with a local value; names the ones without one. With
// --dependabot, items marked for Dependabot are set there too. With --prune,
// retired names still on GitHub are deleted.
export function push({
  home,
  env = process.env,
  gh = 'gh',
  dependabot = false,
  prune = false,
  workspaceRoot = root,
  log = console.log
}) {
  const { rows, stale, config } = inventory({
    home,
    env,
    gh,
    dependabot,
    workspaceRoot
  });
  const skipped = [];
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
  if (prune)
    for (const entry of stale) {
      const { kind, env: environment } = ghScope(entry.raw);
      deleteOne(gh, kind, entry.name, { env: environment });
      log(`deleted ${entry.scope} ${entry.name}`);
    }
  else if (stale.length)
    log(
      `Left on GitHub (pass --prune to delete): ${stale.map((entry) => `${entry.scope} ${entry.name}`).join('; ')}`
    );
  for (const row of skipped)
    log(
      `skipped ${row.scope} ${row.item.name}: no local value (${row.source})`
    );
  return { set: rows.length - skipped.length, skipped: skipped.length };
}
