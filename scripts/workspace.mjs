import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import {
  root,
  configHome,
  assertExternal,
  importCheckout,
  initializeConfig,
  legacyKeysPresent,
  migrateConfig,
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
} from './local-config.mjs';
import { formatInventory, inventory, push } from './secrets.mjs';

// One line per config.ini section, derived from the section table.
function sectionHelp() {
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
        if (lines.at(-1).length + word.length > 80) lines.push(' '.repeat(19));
        lines[lines.length - 1] +=
          `${lines.at(-1).endsWith(' ') ? '' : ' '}${word}`;
      }
      return lines.map((line) => line.trimEnd()).join('\n');
    })
    .join('\n');
}

const help = `Workspace setup (Node only; no npm dependency required)

  node scripts/workspace.mjs setup [--from OLD_CHECKOUT] [--skip-deps]
  node scripts/workspace.mjs doctor
  node scripts/workspace.mjs run PROFILE -- COMMAND [ARG...]
  node scripts/workspace.mjs secrets check [--dependabot]
  node scripts/workspace.mjs secrets push [--dependabot] [--prune]

just setup, just doctor, just secrets-check and just secrets-sync are equivalent
shortcuts. just with-config PROFILE COMMAND [ARG...] runs a command from the
repository root with scoped configuration.

Configuration: BPP_CONFIG_HOME or ~/.config/bazaarplusplus (outside checkouts).
  config.ini     every value, one section per purpose:
${sectionHelp()}
  keys/          tauri-updater.key, tauri-updater.key.pub, AuthKey_<APPLE_API_KEY>.p8,
                 developer-id.p12
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
S3 pair every store needs is derived from the token (release/r2-store.mjs).
signing stages [signing] and keys/ into a private temporary BPP_SIGNING_SECRETS_DIR
removed after the command; an exported BPP_SIGNING_SECRETS_DIR is used as given.
Edit config.ini, then rerun setup --skip-deps. Local projection edits conflict.
--from imports an old checkout's analyzer .env, server .dev.vars and installer
signing-secrets/ into config.ini and keys/ under the same value rules, refuses
differing existing values, preserves data path meaning, and never deletes source
files or creates data stores.
setup also migrates the earlier layout in place: [release] BPP_R2_ACCOUNT_ID and
[analyzer] BPP_METRICS_R2_ACCOUNT_ID become [cloudflare] CLOUDFLARE_ACCOUNT_ID,
[analyzer] BPP_BUNDLE_SYNC_TOKEN joins [server] BUNDLE_SYNC_TOKEN (differing values
are refused), and the derived R2 key pairs stay as commented lines in [cloudflare]
until you delete them; nothing is dropped silently.

secrets check lists, by name only, what each GitHub scope holds and what config.ini
and keys/ hold, following release/github-secrets.json; secrets push copies every
item with a local value through gh (values over stdin, never printed). --dependabot
also sets the mod lane token for Dependabot; --prune deletes the retired names the
table lists. Tokens are created in the Cloudflare dashboard (My Profile > API Tokens
or Account > Manage API tokens): the operator token with Object Read & Write on
bppinstaller, bazaarplusplus-game-libs and the metrics bucket plus Workers Scripts
Edit; the read-only token with Object Read on bazaarplusplus-game-libs only.

setup installs each Node project's locked dependencies, the analyzer environment,
the mod's local .NET tools and Git hooks. Install language/platform tools first.
It never runs analysis, signs packages, deploys, publishes, or exports Keychain keys.
doctor checks local prerequisites only; presence is not proof of remote permission.
POSIX files use mode 600 and config directories 700; Windows ACLs remain user-managed.
`;

function invocation(command, args) {
  // Git Bash supplies npm as an executable script on Windows. Bash receives
  // positional arguments, never a command interpolated with credential values.
  return process.platform === 'win32' && command === 'npm'
    ? ['bash', ['-c', 'exec npm "$@"', 'npm', ...args]]
    : [command, args];
}

function execute(command, args, { cwd = root, env = process.env } = {}) {
  const [binary, parameters] = invocation(command, args);
  const result = spawnSync(binary, parameters, { cwd, env, stdio: 'inherit' });
  if (result.error)
    throw new Error(
      `Cannot start ${command}; check the toolchain installation`
    );
  if (result.status !== 0) {
    const error = new Error(
      `${command} failed${result.signal ? ` (${result.signal})` : ''}`
    );
    error.exitCode = result.status || 1;
    throw error;
  }
}

function probe(command, args, cwd = root) {
  const [binary, parameters] = invocation(command, args);
  return spawnSync(binary, parameters, {
    cwd,
    encoding: 'utf8',
    timeout: 15_000
  });
}

export function doctor(
  home = configHome(),
  repository = root,
  env = process.env
) {
  assertExternal(home, [repository]);
  const rows = [];
  const add = (scope, ok, detail) => rows.push({ scope, ok, detail });
  for (const [command, args] of [
    ['node', ['--version']],
    ['npm', ['--version']],
    ['just', ['--version']],
    ['dotnet', ['--version']],
    ['rustup', ['--version']],
    ['uv', ['--version']]
  ]) {
    const result = probe(command, args, repository);
    const version =
      result.status === 0
        ? result.stdout.trim().split('\n')[0]
        : 'missing or unavailable';
    add('tools', result.status === 0, `${command}: ${version}`);
  }
  for (const project of ['', 'installer', 'site', 'server', 'analyzer']) {
    const directory = project
      ? path.join(repository, `bazaarplusplus-${project}`)
      : repository;
    const installed = fs.existsSync(
      path.join(directory, project === 'analyzer' ? '.venv' : 'node_modules')
    );
    add(
      'dependencies',
      installed,
      `${project || 'root'}: ${installed ? 'installed (not validated)' : 'run just setup'}`
    );
  }
  let config;
  try {
    config = readConfig(home);
  } catch (error) {
    add('config', false, error.message);
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
    add('projections', false, error.message);
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
  const signing = resolveSigning(home, env, repository);
  add(
    'updater signing',
    Boolean(
      env.TAURI_SIGNING_PRIVATE_KEY ||
      (fs.existsSync(signing.updaterKey) &&
        fs.statSync(signing.updaterKey).size > 0)
    ),
    'private key presence only; password/signature not validated'
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
  const legacy = legacyKeysPresent(config);
  if (legacy.length)
    add(
      'config',
      false,
      `earlier layout keys ${legacy.join(', ')}: run just setup --skip-deps to migrate`
    );
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

function printDoctor(home) {
  console.log(`Configuration: ${home}`);
  for (const row of doctor(home))
    console.log(`${row.ok ? 'OK' : 'MISSING'} [${row.scope}] ${row.detail}`);
  console.log(
    'Local inventory only; no remote permissions, builds or runtime health were verified.'
  );
}

export function main(args = process.argv.slice(2)) {
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
    let source;
    let skipDeps = false;
    for (let i = 0; i < rest.length; i++) {
      if (rest[i] === '--skip-deps') skipDeps = true;
      else if (rest[i] === '--from' && rest[i + 1])
        source = path.resolve(rest[++i]);
      else throw new Error('Usage: setup [--from OLD_CHECKOUT] [--skip-deps]');
    }
    if (source) {
      const imported = importCheckout(source, home);
      console.log(`Imported ${imported.join(', ')}; originals preserved.`);
    } else {
      const migrated = migrateConfig(home);
      if (migrated.length)
        console.log(
          `Migrated config.ini to the current layout: ${migrated.join(', ')}.`
        );
    }
    initializeConfig(home);
    refreshProjections(home);
    if (!skipDeps) {
      for (const project of ['', 'installer', 'site', 'server']) {
        execute('npm', ['ci'], {
          cwd: project ? path.join(root, `bazaarplusplus-${project}`) : root
        });
      }
      execute('uv', ['sync', '--locked'], {
        cwd: path.join(root, 'bazaarplusplus-analyzer')
      });
      execute('dotnet', ['tool', 'restore'], {
        cwd: path.join(root, 'bazaarplusplus-mod')
      });
      execute(process.execPath, ['scripts/install-hooks.mjs']);
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
    const allowed =
      rest[0] === 'push' ? ['--dependabot', '--prune'] : ['--dependabot'];
    for (const flag of flags)
      if (!allowed.includes(flag))
        throw new Error(
          `Usage: secrets check [--dependabot] | secrets push [--dependabot] [--prune]`
        );
    const dependabot = flags.includes('--dependabot');
    const gh = process.env.BPP_GH_BIN || 'gh';
    if (rest[0] === 'check') {
      console.log(
        formatInventory(inventory({ home, gh, dependabot }), { dependabot })
      );
      return;
    }
    const result = push({
      home,
      gh,
      dependabot,
      prune: flags.includes('--prune')
    });
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
    console.error(error.message);
    process.exitCode = error.exitCode || 1;
  }
}
