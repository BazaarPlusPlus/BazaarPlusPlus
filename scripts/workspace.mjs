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
  readConfig,
  sectionValues,
  appleKeyPath,
  stageSigning,
  refreshProjections,
  projectionChanges,
  commandEnvironment
} from './local-config.mjs';

const help = `Workspace setup (Node only; no npm dependency required)

  node scripts/workspace.mjs setup [--from OLD_CHECKOUT] [--skip-deps]
  node scripts/workspace.mjs doctor
  node scripts/workspace.mjs run PROFILE -- COMMAND [ARG...]

just setup and just doctor are equivalent shortcuts. just with-config PROFILE
COMMAND [ARG...] runs a command from the repository root with scoped configuration.

Configuration: BPP_CONFIG_HOME or ~/.config/bazaarplusplus (outside checkouts).
  config.ini     every value, one section per purpose:
    [server]       projects verbatim to bazaarplusplus-server/.dev.vars
    [analyzer]     projects verbatim to bazaarplusplus-analyzer/.env
    [release]      BPP_R2_ACCOUNT_ID, BPP_R2_ACCESS_KEY_ID, BPP_R2_SECRET_ACCESS_KEY
    [signing]      APPLE_SIGNING_IDENTITY, APPLE_API_ISSUER, APPLE_API_KEY,
                   optional APPLE_API_KEY_PATH (relative to this directory),
                   optional TAURI_SIGNING_PRIVATE_KEY_PASSWORD
    [machine]      optional BPP_MANAGED_PATH, BPP_GAME_ROOT (profile: mod)
    [cloudflare]   optional CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID
  keys/          tauri-updater.key, tauri-updater.key.pub, AuthKey_<APPLE_API_KEY>.p8
Any other file in the directory is yours; tooling neither reads nor writes it.

Profiles: release, signing, mod, cloudflare, server, analyzer.
Exported variables take precedence. config.ini is parsed as data, never sourced;
unknown sections, non KEY=value lines and multi-line quoted values are errors.
server/analyzer profiles refresh managed projections before running the command.
signing stages [signing] and keys/ into a private temporary BPP_SIGNING_SECRETS_DIR
removed after the command; an exported BPP_SIGNING_SECRETS_DIR is used as given.
Edit config.ini, then rerun setup --skip-deps. Local projection edits conflict.
--from imports an old checkout's analyzer .env, server .dev.vars and installer
signing-secrets/ into config.ini and keys/, refuses differing existing values,
preserves data path meaning, and never deletes source files or creates data stores.

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
  const required = (scope, section, keys, ambient = {}) => {
    const values = { ...sectionValues(config, section) };
    for (const key of keys) if (ambient[key]) values[key] = ambient[key];
    const missing = keys.filter((key) => !values[key]?.trim());
    add(
      scope,
      missing.length === 0,
      missing.length
        ? `missing: ${missing.join(', ')}`
        : 'fields present; remote permissions unverified'
    );
    return values;
  };
  const analyzer = required('analyzer', 'analyzer', [
    'BPP_DATA_ROOT',
    'BPP_V5_API_BASE_URL',
    'BPP_BUNDLE_SYNC_TOKEN'
  ]);
  if (analyzer.BPP_DATA_ROOT) {
    const data = path.resolve(
      repository,
      'bazaarplusplus-analyzer',
      analyzer.BPP_DATA_ROOT
    );
    add(
      'analyzer data',
      fs.existsSync(data),
      fs.existsSync(data)
        ? 'configured state path exists; integrity unverified'
        : 'configured state path is missing; no empty replacement was created'
    );
  }
  required('analyzer publish', 'analyzer', [
    'BPP_METRICS_R2_ACCOUNT_ID',
    'BPP_METRICS_R2_BUCKET',
    'BPP_METRICS_R2_ACCESS_KEY_ID',
    'BPP_METRICS_R2_SECRET_ACCESS_KEY'
  ]);
  required('server', 'server', [
    'R2_PRESIGN_ACCESS_KEY_ID',
    'R2_PRESIGN_SECRET_ACCESS_KEY',
    'BUNDLE_SYNC_TOKEN',
    'BAZAARDB_DELIVERY_TOKEN'
  ]);
  required(
    'release upload',
    'release',
    ['BPP_R2_ACCOUNT_ID', 'BPP_R2_ACCESS_KEY_ID', 'BPP_R2_SECRET_ACCESS_KEY'],
    env
  );
  // Mirrors what bundle.sh will see: an exported directory as given, else
  // [signing] with keys/, each overridden by exported values.
  const explicit = env.BPP_SIGNING_SECRETS_DIR;
  const fileValue = (name) => {
    const file = path.join(explicit, name);
    return fs.existsSync(file) ? fs.readFileSync(file, 'utf8').trim() : '';
  };
  const signing = explicit
    ? {
        APPLE_API_ISSUER: fileValue('apple-api-issuer'),
        APPLE_API_KEY: fileValue('apple-api-key'),
        APPLE_API_KEY_PATH: fileValue('apple-api-key-path')
      }
    : sectionValues(config, 'signing');
  for (const key of ['APPLE_API_ISSUER', 'APPLE_API_KEY', 'APPLE_API_KEY_PATH'])
    if (env[key]) signing[key] = env[key];
  const keyDirectory = explicit || path.join(home, 'keys');
  const updaterKey = path.join(keyDirectory, 'tauri-updater.key');
  add(
    'updater signing',
    Boolean(
      env.TAURI_SIGNING_PRIVATE_KEY ||
      (fs.existsSync(updaterKey) && fs.statSync(updaterKey).size > 0)
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
    // bundle.sh resolves an exported relative path against the installer.
    const keyPath = env.APPLE_API_KEY_PATH
      ? path.resolve(
          repository,
          'bazaarplusplus-installer',
          env.APPLE_API_KEY_PATH
        )
      : explicit
        ? signing.APPLE_API_KEY_PATH ||
          path.join(explicit, `AuthKey_${signing.APPLE_API_KEY}.p8`)
        : appleKeyPath(home, signing);
    const available = Boolean(
      signing.APPLE_API_ISSUER &&
      signing.APPLE_API_KEY &&
      keyPath &&
      fs.existsSync(keyPath)
    );
    add(
      'Apple notarization',
      available,
      available
        ? 'fields and key file present; remote authorization unverified'
        : 'issuer, key id or referenced .p8 missing'
    );
  }
  const cf = commandEnvironment('cloudflare', home, env, repository);
  add(
    'Cloudflare CLI',
    Boolean(cf.CLOUDFLARE_API_TOKEN),
    cf.CLOUDFLARE_API_TOKEN
      ? 'API token present; permissions unverified'
      : 'no API token configured; Wrangler OAuth may still be available'
  );
  const managed = commandEnvironment(
    'mod',
    home,
    env,
    repository
  ).BPP_MANAGED_PATH;
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
    if (rest[0] !== 'signing') {
      execute(rest[2], rest.slice(3), {
        cwd: process.cwd(),
        env: commandEnvironment(rest[0], home)
      });
      return;
    }
    // The child shares the terminal's signals; outlive them so staged
    // signing files are always removed.
    const hold = () => {};
    const signals = ['SIGINT', 'SIGTERM', 'SIGHUP'];
    const staged = stageSigning(home);
    for (const signal of signals) process.on(signal, hold);
    try {
      execute(rest[2], rest.slice(3), { cwd: process.cwd(), env: staged.env });
    } finally {
      staged.cleanup();
      for (const signal of signals) process.off(signal, hold);
    }
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
