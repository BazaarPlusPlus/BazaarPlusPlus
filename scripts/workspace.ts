import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import type { SpawnSyncReturns } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import {
  root,
  configHome,
  assertExternal,
  initializeConfig,
  readConfig,
  sections,
  profiles,
  sectionKeys,
  resolveValues,
  missingValues,
  resolveSigning,
  refreshProjections,
  projectionChanges,
  withProfileEnvironment
} from './local-config.ts';
import { formatInventory, inventory, push } from './secrets.ts';

/** One doctor line: its scope, whether it passed, and what it found. */
export interface DoctorRow {
  scope: string;
  ok: boolean;
  detail: string;
}

// An error that names the exit code the CLI reports.
interface ExitError extends Error {
  exitCode?: number;
}

interface ExecuteOptions {
  cwd?: string;
  env?: NodeJS.ProcessEnv;
}

// One line per config.ini section, derived from the section table.
function sectionHelp(): string {
  return Object.entries(sections)
    .map(([name, spec]) => {
      const keys = sectionKeys(spec);
      const optional = spec.optional === true ? keys : spec.optional || [];
      const list = [
        keys.filter((key) => !optional.includes(key)).join(', '),
        optional.length ? `optional ${optional.join(', ')}` : ''
      ]
        .filter(Boolean)
        .join(', ');
      const projected = (spec.projected || [])
        .map(({ from, key, as = key }) => `${as} from [${from}] ${key}`)
        .join(', ');
      const what = spec.projection
        ? `projects to ${spec.projection}${projected ? `, appending ${projected}` : ''}`
        : spec.stage
          ? `${list}; staged as files except APPLE_CERTIFICATE_PASSWORD`
          : list;
      const profile =
        spec.profile === name ? '' : ` (profile: ${spec.profile})`;
      const words = `${what}${profile}`.split(' ');
      const lines = [`    ${`[${name}]`.padEnd(15)}`];
      for (const word of words) {
        if (lines[lines.length - 1].length + word.length > 80)
          lines.push(' '.repeat(19));
        lines[lines.length - 1] +=
          `${lines[lines.length - 1].endsWith(' ') ? '' : ' '}${word}`;
      }
      return lines.map((line) => line.trimEnd()).join('\n');
    })
    .join('\n');
}

const help = `Workspace setup (Bun only; no package dependencies required)

  bun scripts/workspace.ts setup [--skip-deps]
  bun scripts/workspace.ts doctor
  bun scripts/workspace.ts run PROFILE -- COMMAND [ARG...]
  bun scripts/workspace.ts secrets check [--dependabot]
  bun scripts/workspace.ts secrets push [--dependabot]

just setup, just doctor, just secrets-check and just secrets-sync are equivalent
shortcuts. just with-config PROFILE COMMAND [ARG...] runs a command from the
repository root with scoped configuration.

Configuration: BPP_CONFIG_HOME or ~/.config/bazaarplusplus (outside checkouts).
  config.ini     every value, one section per purpose:
${sectionHelp()}
  keys/          tauri-updater.key, tauri-updater.key.pub, AuthKey_<APPLE_API_KEY>.p8,
                 developer-id.p12, android-release.p12
A relative APPLE_API_KEY_PATH resolves against the configuration directory.
Any other file in the directory is yours; tooling neither reads nor writes it.

Profiles: ${profiles.join(', ')}.
Exported variables take precedence. config.ini is parsed as data, never sourced.
Each section holds KEY=value lines (optionally export KEY=value) and # comments.
Values keep to the form dotenv readers of the managed copies agree on:
  KEY=value      unquoted, trimmed; whitespace then # starts a comment
  KEY='value'    literal; no ', no \\\\ and no trailing \\
  KEY="value"    literal; no " and no backslash (use single quotes or leave it
                 unquoted, as for Windows paths)
Errors name the line, never the value: unknown or malformed section headers,
KEY=value outside a section, duplicate sections or keys, ; comments, backtick
quotes, a value starting with #, an unquoted # without a space before it, text
after a closing quote, unterminated or multi-line quotes, and a CR or NUL
inside a line.
server/analyzer profiles refresh managed projections before running the command.
release exports [cloudflare] CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID; the R2
S3 pair every store needs is derived from the token (release/r2-store.ts).
signing stages [signing] and keys/ into a private temporary BPP_SIGNING_SECRETS_DIR
removed after the command; an exported BPP_SIGNING_SECRETS_DIR is used as given.
Edit config.ini, then rerun setup --skip-deps. Local projection edits conflict.

secrets check lists, by name only, what each GitHub scope holds and what config.ini
and keys/ hold, following release/github-secrets.json; secrets push copies every
item with a local value through gh (values over stdin, never printed). --dependabot
also sets CLOUDFLARE_API_TOKEN for Dependabot. The token is an Account API Token
(Account > Manage API tokens) with Object Read & Write on every bucket plus Workers
Scripts Edit.

setup installs each JS/TS project's locked dependencies, the analyzer environment,
the mod's local .NET tools and Git hooks. Install language/platform tools first.
It never runs analysis, signs packages, deploys, publishes, or exports Keychain keys.
doctor checks local prerequisites only; presence is not proof of remote permission.
POSIX files use mode 600 and config directories 700; Windows ACLs remain user-managed.
`;

function execute(
  command: string,
  args: string[],
  { cwd = root, env = process.env }: ExecuteOptions = {}
): void {
  const result = spawnSync(command, args, { cwd, env, stdio: 'inherit' });
  if (result.error)
    throw new Error(
      `Cannot start ${command}; check the toolchain installation`
    );
  if (result.status !== 0) {
    const error: ExitError = new Error(
      `${command} failed${result.signal ? ` (${result.signal})` : ''}`
    );
    error.exitCode = result.status || 1;
    throw error;
  }
}

function probe(
  command: string,
  args: string[],
  cwd = root
): SpawnSyncReturns<string> {
  return spawnSync(command, args, {
    cwd,
    encoding: 'utf8',
    timeout: 15_000
  });
}

// The tool versions the checkout pins: Bun in the root package.json engines,
// uv in the analyzer's required-version. A missing file pins nothing.
export function pinnedToolVersions(
  repository: string
): Partial<Record<'bun' | 'uv', string>> {
  const read = (file: string): string => {
    try {
      return fs.readFileSync(path.join(repository, file), 'utf8');
    } catch {
      return '';
    }
  };
  const pins: Partial<Record<'bun' | 'uv', string>> = {};
  try {
    const bun = JSON.parse(read('package.json') || '{}').engines?.bun;
    if (typeof bun === 'string') pins.bun = bun;
  } catch {
    // An unreadable package.json fails the commands that need it.
  }
  const uv = /^required-version\s*=\s*"==([^"]+)"/m.exec(
    read('bazaarplusplus-analyzer/pyproject.toml')
  );
  if (uv) pins.uv = uv[1];
  return pins;
}

const escapeRegExp = (text: string): string =>
  text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// Direct dependencies whose node_modules copy is missing or differs from the
// version bun.lock resolves. A dependency bun.lock does not list is skipped.
export function staleDependencies(directory: string): string[] {
  let manifest: Record<string, Record<string, string> | undefined>;
  let lock: string;
  try {
    manifest = JSON.parse(
      fs.readFileSync(path.join(directory, 'package.json'), 'utf8')
    );
    lock = fs.readFileSync(path.join(directory, 'bun.lock'), 'utf8');
  } catch {
    return [];
  }
  const names = Object.keys({
    ...manifest.dependencies,
    ...manifest.devDependencies
  });
  return names.filter((name) => {
    const quoted = escapeRegExp(name);
    const locked = new RegExp(
      `^ {4}"${quoted}": \\["${quoted}@([^"]+)"`,
      'm'
    ).exec(lock)?.[1];
    if (locked === undefined) return false;
    try {
      const installed = JSON.parse(
        fs.readFileSync(
          path.join(directory, 'node_modules', name, 'package.json'),
          'utf8'
        )
      ).version;
      return installed !== locked;
    } catch {
      return true;
    }
  });
}

export function doctor(
  home = configHome(),
  repository = root,
  env: NodeJS.ProcessEnv = process.env
): DoctorRow[] {
  assertExternal(home, [repository]);
  const rows: DoctorRow[] = [];
  const add = (scope: string, ok: boolean, detail: string) =>
    rows.push({ scope, ok, detail });
  const pins = pinnedToolVersions(repository);
  for (const [command, args] of [
    ['bun', ['--version']],
    ['just', ['--version']],
    ['dotnet', ['--version']],
    ['rustup', ['--version']],
    ['uv', ['--version']]
  ] satisfies [string, string[]][]) {
    const result = probe(command, args, repository);
    const version =
      result.status === 0
        ? result.stdout.trim().split('\n')[0]
        : 'missing or unavailable';
    const pin = pins[command as keyof typeof pins];
    const matches = pin === undefined || version.split(/\s+/).includes(pin);
    add(
      'tools',
      result.status === 0 && matches,
      matches
        ? `${command}: ${version}`
        : `${command}: ${version}; the checkout pins ${pin}`
    );
  }
  for (const project of ['', 'installer', 'site', 'server', 'analyzer']) {
    const directory = project
      ? path.join(repository, `bazaarplusplus-${project}`)
      : repository;
    const installed = fs.existsSync(
      path.join(directory, project === 'analyzer' ? '.venv' : 'node_modules')
    );
    const stale =
      installed && project !== 'analyzer' ? staleDependencies(directory) : [];
    add(
      'dependencies',
      installed && stale.length === 0,
      `${project || 'root'}: ${
        !installed
          ? 'run just setup'
          : stale.length
            ? `differs from bun.lock (${stale.join(', ')}); run just setup`
            : project === 'analyzer'
              ? 'installed (not validated)'
              : 'direct dependencies match bun.lock'
      }`
    );
  }
  let config;
  try {
    config = readConfig(home);
  } catch (error) {
    add('config', false, (error as Error).message);
    return rows;
  }
  add(
    'config',
    config.sections.size > 0,
    config.sections.size > 0
      ? `config.ini sections: ${[...config.sections.keys()].join(', ')}`
      : 'config.ini missing; run just setup'
  );
  try {
    for (const change of projectionChanges(home, repository)) {
      add(
        'projections',
        !change.changed,
        `${change.relative}: ${change.changed ? 'run just setup --skip-deps' : 'matches central config'}`
      );
    }
  } catch (error) {
    add('projections', false, (error as Error).message);
  }
  for (const { scope, missing } of missingValues(config, env)) {
    add(
      scope,
      missing.length === 0,
      missing.length
        ? `missing: ${missing.join(', ')}`
        : 'fields present; remote permissions unverified'
    );
  }
  // A relative path already failed the projections row above.
  const data = config.sections.get('analyzer')?.values.BPP_DATA_ROOT;
  if (data && path.isAbsolute(data)) {
    add(
      'analyzer data',
      fs.existsSync(data),
      fs.existsSync(data)
        ? 'configured state path exists; integrity unverified'
        : 'configured state path is missing; no empty replacement was created'
    );
  }
  const signing = resolveSigning(home, env);
  add(
    'updater signing',
    Boolean(
      env.TAURI_SIGNING_PRIVATE_KEY ||
      (fs.existsSync(signing.updaterKey) &&
        fs.statSync(signing.updaterKey).size > 0)
    ),
    'private key presence only; signature not validated'
  );
  if (process.platform === 'darwin') {
    const result = probe('security', [
      'find-identity',
      '-v',
      '-p',
      'codesigning'
    ]);
    const identities = (result.stdout || '')
      .split('\n')
      .filter((line) => line.includes('Developer ID Application:'));
    add(
      'Apple Keychain',
      result.status === 0 && identities.length > 0,
      `${identities.length} Developer ID Application identities available`
    );
    const available = Boolean(
      signing.values.APPLE_API_ISSUER &&
      signing.values.APPLE_API_KEY &&
      signing.appleKeyPath &&
      fs.existsSync(signing.appleKeyPath)
    );
    add(
      'Apple notarization',
      available,
      available
        ? 'fields and key file present; remote authorization unverified'
        : 'issuer, key id or referenced .p8 missing'
    );
  }
  const managed = resolveValues(config, 'machine', env).BPP_MANAGED_PATH;
  if (managed) {
    add(
      'game assemblies',
      fs.existsSync(path.join(managed, 'Assembly-CSharp.dll')),
      'configured Managed path'
    );
  } else {
    const detected = probe(
      'dotnet',
      [
        'msbuild',
        'build/ManagedPath.props',
        '-nologo',
        '-getProperty:ManagedPath'
      ],
      path.join(repository, 'bazaarplusplus-mod')
    );
    const directory = (detected.stdout || '').trim();
    add(
      'game assemblies',
      detected.status === 0 &&
        fs.existsSync(path.join(directory, 'Assembly-CSharp.dll')),
      'project-owned Steam discovery'
    );
  }
  return rows;
}

function printDoctor(home: string): void {
  console.log(`Configuration: ${home}`);
  for (const row of doctor(home))
    console.log(`${row.ok ? 'OK' : 'MISSING'} [${row.scope}] ${row.detail}`);
  console.log(
    'Local inventory only; no remote permissions, builds or runtime health were verified.'
  );
}

export function main(args: string[] = process.argv.slice(2)): void {
  const [command, ...rest] = args;
  if (!command || command === '--help' || command === 'help') {
    console.log(help);
    return;
  }
  const home = configHome();
  assertExternal(home);
  if (command === 'doctor' && rest.length === 0) {
    printDoctor(home);
    return;
  }
  if (command === 'setup') {
    let skipDeps = false;
    for (const arg of rest) {
      if (arg === '--skip-deps') skipDeps = true;
      else throw new Error('Usage: setup [--skip-deps]');
    }
    initializeConfig(home);
    refreshProjections(home);
    if (!skipDeps) {
      for (const project of ['', 'installer', 'site', 'server']) {
        execute('bun', ['install', '--frozen-lockfile'], {
          cwd: project ? path.join(root, `bazaarplusplus-${project}`) : root
        });
      }
      execute('uv', ['sync', '--locked'], {
        cwd: path.join(root, 'bazaarplusplus-analyzer')
      });
      execute('dotnet', ['tool', 'restore'], {
        cwd: path.join(root, 'bazaarplusplus-mod')
      });
      execute(process.execPath, ['scripts/install-hooks.ts']);
    }
    printDoctor(home);
    return;
  }
  if (command === 'run' && rest[1] === '--' && rest[2]) {
    withProfileEnvironment(rest[0], home, (env) =>
      execute(rest[2], rest.slice(3), { cwd: process.cwd(), env })
    );
    return;
  }
  if (command === 'secrets' && ['check', 'push'].includes(rest[0])) {
    const flags = rest.slice(1);
    for (const flag of flags)
      if (flag !== '--dependabot')
        throw new Error(
          `Usage: secrets check [--dependabot] | secrets push [--dependabot]`
        );
    const dependabot = flags.includes('--dependabot');
    const gh = process.env.BPP_GH_BIN || 'gh';
    if (rest[0] === 'check') {
      console.log(
        formatInventory(inventory({ home, gh, dependabot }), { dependabot })
      );
      return;
    }
    const result = push({ home, gh, dependabot });
    console.log(
      `Set ${result.set} GitHub names; ${result.skipped} without a local value.`
    );
    if (result.skipped) process.exitCode = 1;
    return;
  }
  throw new Error('Unknown command or arguments; use --help');
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  try {
    main();
  } catch (error) {
    console.error((error as ExitError).message);
    process.exitCode = (error as ExitError).exitCode || 1;
  }
}
