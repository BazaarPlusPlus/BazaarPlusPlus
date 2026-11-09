import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { parseEnv } from 'node:util';
import { stringify } from 'smol-toml';
import { test } from 'vitest';
import type { TestContext } from 'vitest';
import {
  root,
  initializeConfig,
  refreshProjections,
  withProfileEnvironment,
  readConfig,
  parseConfig,
  projectionChanges,
  assertExternal,
  renderConfig
} from './local-config.ts';
import { doctor, pinnedToolVersions, staleDependencies } from './workspace.ts';

function fixture(t: TestContext) {
  const directory = fs.realpathSync(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp setup '))
  );
  t.onTestFinished(() =>
    fs.rmSync(directory, { recursive: true, force: true })
  );
  const repository = path.join(directory, 'checkout');
  const scratch = path.join(directory, 'scratch dir');
  const home = path.join(directory, 'config');
  for (const project of ['analyzer', 'server', 'installer']) {
    fs.mkdirSync(path.join(repository, `bazaarplusplus-${project}`), {
      recursive: true
    });
  }
  fs.writeFileSync(
    path.join(repository, 'bazaarplusplus-analyzer/.env.example'),
    'BPP_DATA_ROOT=/absolute/path/to/data\n'
  );
  fs.mkdirSync(path.join(repository, 'release'));
  fs.copyFileSync(
    path.join(root, 'release/github-secrets.json'),
    path.join(repository, 'release/github-secrets.json')
  );
  fs.mkdirSync(scratch);
  const put = (base: string, file: string, text: string | Buffer): string => {
    const target = path.join(base, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
    return target;
  };
  return { directory, repository, scratch, home, put };
}

type Fixture = ReturnType<typeof fixture>;

const tomlConfig = (sections: Record<string, string>): string =>
  stringify(
    Object.fromEntries(
      Object.entries(sections).map(([name, body]) => [name, parseEnv(body)])
    )
  );

const projectedMarker =
  '# Projected from config.toml [server] and [cloudflare]; edit there.';

test('new setup needs no analyzer fields and quotes empty credentials', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, root);
  const config = readConfig(f.home);
  assert.equal(config.sections.has('analyzer'), false);
  assert.equal(
    config.sections.get('cloudflare')!.values.CLOUDFLARE_API_TOKEN,
    ''
  );
});

test('setup preserves config.toml, tightens POSIX permissions and removes only stale signing staging', (t) => {
  const f = fixture(t);
  const text = tomlConfig({ cloudflare: 'CLOUDFLARE_API_TOKEN=KEEP\n' });
  const file = f.put(f.home, 'config.toml', text);
  fs.chmodSync(file, 0o644);
  f.put(f.home, '.signing-stale/apple-api-key', 'OLD');
  f.put(f.home, '.signing-running/apple-api-key', 'LIVE');
  const old = new Date(Date.now() - 2 * 24 * 60 * 60 * 1000);
  fs.utimesSync(path.join(f.home, '.signing-stale'), old, old);
  initializeConfig(f.home, f.repository);
  assert.equal(fs.existsSync(path.join(f.home, '.signing-running')), true);
  assert.equal(fs.readFileSync(file, 'utf8'), text);
  assert.equal(fs.existsSync(path.join(f.home, '.signing-stale')), false);
  if (process.platform !== 'win32')
    assert.equal(fs.statSync(file).mode & 0o777, 0o600);
});

test('TOML rejects ambiguous credentials and unsupported settings without echoing values', () => {
  for (const text of [
    '[server]\nA = "SECRET"\nA = "other"\n',
    'server = "SECRET"\n',
    '[server]\nA = ["SECRET"]\n',
    '[server]\nA = 123\n',
    '[server]\nA = true\n',
    '[server]\nA = 2026-11-09\n',
    '[server]\nA = "SECRET\n',
    '[server]\nA = """SECRET\nsecond line"""\n',
    '[unknown]\nA = "SECRET"\n',
    '[analyzer]\nBPP_API_BASE_URL = "SECRET"\n'
  ])
    assert.throws(
      () => parseConfig(text),
      (error: Error) => {
        assert.doesNotMatch(error.message, /SECRET/);
        return true;
      }
    );
});

test('TOML round trips quoted identifiers, special credentials and Windows paths', () => {
  const values = {
    BPP_GAME_EMAIL: '00123',
    BPP_GAME_PASSWORD: 'true # \" unicode 密码',
    BPP_GAME_ROOT: String.raw`D:\new games\The Bazaar`
  };
  const config = parseConfig(stringify({ game: values, analyzer: {} }));
  assert.deepEqual(config.sections.get('game')!.values, values);
  assert.deepEqual(parseConfig(renderConfig(config)), config);
});

test('central edits refresh before use; local edits cause conflict before any projection is replaced', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, f.repository);
  refreshProjections(f.home, f.repository);
  const analyzerEnv = path.join(f.repository, 'bazaarplusplus-analyzer/.env');
  const projected = (sync: string, token: string, account: string): string =>
    `${projectedMarker}\nBPP_BUNDLE_SYNC_TOKEN="${sync}"\nCLOUDFLARE_API_TOKEN="${token}"\nCLOUDFLARE_ACCOUNT_ID="${account}"\n`;
  f.put(
    f.home,
    'config.toml',
    tomlConfig({
      server: 'BUNDLE_SYNC_TOKEN=shared\n',
      analyzer: 'BPP_DATA_ROOT=/changed\n',
      cloudflare: 'CLOUDFLARE_API_TOKEN=cf-token\n'
    })
  );
  const env = withProfileEnvironment(
    'analyzer',
    f.home,
    (env) => env,
    { PATH: 'p' },
    f.repository
  );
  assert.deepEqual(env, { PATH: 'p' });
  assert.equal(
    fs.readFileSync(analyzerEnv, 'utf8'),
    `BPP_DATA_ROOT="/changed"\n${projected('shared', 'cf-token', '')}`
  );
  f.put(
    f.home,
    'config.toml',
    tomlConfig({ server: '', analyzer: 'BPP_DATA_ROOT=/next\n' })
  );
  f.put(
    f.repository,
    'bazaarplusplus-server/.dev.vars',
    'BUNDLE_SYNC_TOKEN=LOCAL-EDIT\n'
  );
  assert.throws(
    () => withProfileEnvironment('server', f.home, () => {}, {}, f.repository),
    /Local configuration conflict: bazaarplusplus-server\/.dev.vars; preserve your edits in \[server\]/
  );
  assert.equal(
    fs.readFileSync(analyzerEnv, 'utf8'),
    `BPP_DATA_ROOT="/changed"\n${projected('shared', 'cf-token', '')}`
  );
  assert.equal(
    fs.readFileSync(
      path.join(f.repository, 'bazaarplusplus-server/.dev.vars'),
      'utf8'
    ),
    'BUNDLE_SYNC_TOKEN=LOCAL-EDIT\n'
  );
});

test('a local file predating setup is never overwritten without matching central content', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, f.repository);
  f.put(f.repository, 'bazaarplusplus-analyzer/.env', 'BPP_DATA_ROOT=/keep\n');
  assert.throws(
    () => refreshProjections(f.home, f.repository),
    /Local configuration conflict/
  );
  assert.equal(
    fs.existsSync(path.join(f.repository, '.bpp-local.json')),
    false
  );
});

test('scoped environment only injects its section allowlist; explicit environment wins and shell text stays literal', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.toml',
    tomlConfig({
      cloudflare:
        'CLOUDFLARE_ACCOUNT_ID=account\nCLOUDFLARE_API_TOKEN="$(touch never); `exit 1`"\nNODE_OPTIONS=--bad\n',
      machine: 'CLOUDFLARE_ACCOUNT_ID=wrong-section\n',
      game: 'BPP_GAME_EMAIL=test@example.invalid\nBPP_GAME_PASSWORD=secret\n'
    })
  );
  const env = (profile: string, given: NodeJS.ProcessEnv = {}) =>
    withProfileEnvironment(profile, f.home, (env) => env, given, f.repository);
  assert.deepEqual(env('release', { CLOUDFLARE_ACCOUNT_ID: 'explicit' }), {
    CLOUDFLARE_ACCOUNT_ID: 'explicit',
    CLOUDFLARE_API_TOKEN: '$(touch never); `exit 1`'
  });
  assert.deepEqual(env('mod'), {});
  assert.deepEqual(env('game'), {
    BPP_GAME_EMAIL: 'test@example.invalid',
    BPP_GAME_PASSWORD: 'secret'
  });
  assert.throws(
    () => env('cloudflare'),
    /Unknown profile; use server, analyzer, signing, mod, game, release or qqbot/
  );
});

test('omitting analyzer settings still refreshes shared credentials before use', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.toml',
    tomlConfig({
      server: 'BUNDLE_SYNC_TOKEN=shared\n',
      cloudflare: 'CLOUDFLARE_API_TOKEN=cloud\nCLOUDFLARE_ACCOUNT_ID=account\n'
    })
  );
  withProfileEnvironment('analyzer', f.home, () => {}, {}, f.repository);
  const projection = fs.readFileSync(
    path.join(f.repository, 'bazaarplusplus-analyzer/.env'),
    'utf8'
  );
  assert.equal(parseEnv(projection).BPP_BUNDLE_SYNC_TOKEN, 'shared');
  assert.equal(parseEnv(projection).CLOUDFLARE_API_TOKEN, 'cloud');
  assert.equal('BPP_DATA_ROOT' in parseEnv(projection), false);
  assert.equal('BPP_METRICS_R2_BUCKET' in parseEnv(projection), false);
});

test('omitted analyzer section cannot silently run with a stale checkout file', (t) => {
  const f = fixture(t);
  f.put(f.repository, 'bazaarplusplus-analyzer/.env', 'BPP_DATA_ROOT=/old\n');
  assert.throws(
    () =>
      withProfileEnvironment('analyzer', f.home, () => {}, {}, f.repository),
    /Local configuration conflict/
  );
});

test('signing staging holds bundle.sh files only, never injects values, and is removed even when the command fails', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.toml',
    tomlConfig({
      signing:
        "APPLE_API_KEY=K1\nAPPLE_API_ISSUER=issuer\nAPPLE_API_KEY_PATH=keys/Other.p8\nAPPLE_CERTIFICATE_PASSWORD='p\\w'\nANDROID_KEYSTORE_PASSWORD=ks-pass\nANDROID_KEY_ALIAS=bpp\n"
    })
  );
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER');
  f.put(f.home, 'keys/developer-id.p12', 'CERT');
  f.put(f.home, 'keys/android-release.p12', 'KEYSTORE');
  f.put(f.home, 'keys/notes.txt', 'NOT-KEY-MATERIAL');
  let directory: string | undefined;
  const env = withProfileEnvironment(
    'signing',
    f.home,
    (env) => {
      directory = env.BPP_SIGNING_SECRETS_DIR!;
      assert.deepEqual(fs.readdirSync(directory).sort(), [
        'android-key-alias',
        'android-keystore-password',
        'android-release.p12',
        'apple-api-issuer',
        'apple-api-key',
        'apple-api-key-path',
        'tauri-updater.key'
      ]);
      assert.equal(
        fs.readFileSync(path.join(directory, 'android-key-alias'), 'utf8'),
        'bpp'
      );
      assert.equal(
        fs.readFileSync(path.join(directory, 'apple-api-key-path'), 'utf8'),
        path.join(f.home, 'keys/Other.p8')
      );
      if (process.platform !== 'win32')
        assert.equal(fs.statSync(directory).mode & 0o777, 0o700);
      return env;
    },
    { PATH: 'p' },
    f.repository
  );
  assert.deepEqual(Object.keys(env).sort(), [
    'BPP_SIGNING_SECRETS_DIR',
    'PATH'
  ]);
  assert.equal(fs.existsSync(directory!), false);
  assert.throws(() =>
    withProfileEnvironment(
      'signing',
      f.home,
      (env) => {
        directory = env.BPP_SIGNING_SECRETS_DIR;
        throw new Error('command failed');
      },
      {},
      f.repository
    )
  );
  assert.equal(fs.existsSync(directory!), false);
  assert.deepEqual(
    withProfileEnvironment(
      'signing',
      f.home,
      (env) => env,
      { BPP_SIGNING_SECRETS_DIR: '/given' },
      f.repository
    ),
    { BPP_SIGNING_SECRETS_DIR: '/given' }
  );
});

test('config inside a checkout and escaping symlink projections are refused', (t) => {
  const f = fixture(t);
  assert.throws(
    () => assertExternal(path.join(f.repository, 'config'), [f.repository]),
    /outside/
  );
  if (process.platform === 'win32') return;
  fs.symlinkSync(f.repository, f.home);
  assert.throws(() => assertExternal(f.home, [f.repository]), /outside/);
  fs.unlinkSync(f.home);
  initializeConfig(f.home, f.repository);
  const outside = f.put(f.scratch, 'untouched.env', 'KEEP');
  fs.symlinkSync(
    outside,
    path.join(f.repository, 'bazaarplusplus-analyzer/.env')
  );
  assert.throws(() => projectionChanges(f.home, f.repository), /escapes/);
  assert.equal(fs.readFileSync(outside, 'utf8'), 'KEEP');
});

test('doctor derives required keys from config sections and resolves signing as bundle.sh does', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.toml',
    tomlConfig({
      server: 'BUNDLE_SYNC_TOKEN=present\n',
      analyzer: 'BPP_DATA_ROOT=relative/data\n',
      cloudflare: 'CLOUDFLARE_ACCOUNT_ID=present\n'
    })
  );
  f.put(f.repository, 'bazaarplusplus-analyzer/relative/data/.keep', '');
  const secrets = path.join(f.directory, 'explicit secrets');
  f.put(secrets, 'apple-api-issuer', 'issuer\n');
  f.put(secrets, 'apple-api-key', 'K1\n');
  const appleKey = f.put(f.directory, 'apple keys/AuthKey_K1.p8', 'KEY');
  f.put(secrets, 'apple-api-key-path', `${appleKey}\n`);
  f.put(secrets, 'tauri-updater.key', 'UPDATER');
  const rows = doctor(f.home, f.repository, {
    BPP_SIGNING_SECRETS_DIR: secrets,
    BPP_MANAGED_PATH: path.join(f.directory, 'Managed')
  });
  const row = (scope: string) => rows.filter((row) => row.scope === scope);
  assert.ok(
    row('projections').some(
      (row) =>
        !row.ok &&
        /BPP_DATA_ROOT must be absolute in \[analyzer\]/.test(row.detail)
    )
  );
  assert.deepEqual(row('analyzer data'), []);
  assert.deepEqual(row('server'), [
    {
      scope: 'server',
      ok: false,
      detail:
        'missing: R2_PRESIGN_ACCESS_KEY_ID, R2_PRESIGN_SECRET_ACCESS_KEY, BAZAARDB_DELIVERY_TOKEN'
    }
  ]);
  assert.deepEqual(row('release'), [
    { scope: 'release', ok: false, detail: 'missing: CLOUDFLARE_API_TOKEN' }
  ]);
  assert.equal(row('updater signing')[0].ok, true);
  if (process.platform === 'darwin')
    assert.equal(row('Apple notarization')[0].ok, true);
});

test('doctor names direct dependencies that drift from bun.lock and reads the pinned tool versions', (t) => {
  const f = fixture(t);
  const project = path.join(f.repository, 'bazaarplusplus-server');
  f.put(
    project,
    'package.json',
    JSON.stringify({
      dependencies: { current: '^1.0.0', '@scope/old': '^2.0.0' },
      devDependencies: { absent: '^3.0.0', unlocked: '^4.0.0' }
    })
  );
  f.put(
    project,
    'bun.lock',
    [
      '{',
      '  "packages": {',
      '    "current": ["current@1.2.0", "", {}, "sha512-a"],',
      '    "@scope/old": ["@scope/old@2.1.0", "", {}, "sha512-b"],',
      '    "absent": ["absent@3.0.0", "", {}, "sha512-c"],',
      '    "current/nested": ["nested@9.0.0", "", {}, "sha512-d"],',
      '  }',
      '}'
    ].join('\n')
  );
  f.put(project, 'node_modules/current/package.json', '{"version":"1.2.0"}');
  f.put(project, 'node_modules/@scope/old/package.json', '{"version":"2.0.0"}');
  assert.deepEqual(staleDependencies(project), ['@scope/old', 'absent']);
  const server = doctor(f.home, f.repository, {}).find((row) =>
    row.detail.startsWith('server:')
  );
  assert.equal(server?.ok, false);
  assert.match(server?.detail ?? '', /@scope\/old, absent.*just setup/);

  assert.deepEqual(pinnedToolVersions(f.repository), {});
  f.put(f.repository, 'package.json', '{"engines":{"bun":"1.4.4"}}');
  f.put(
    f.repository,
    'bazaarplusplus-analyzer/pyproject.toml',
    '[tool.uv]\nrequired-version = "==0.13.7"\n'
  );
  assert.deepEqual(pinnedToolVersions(f.repository), {
    bun: '1.4.4',
    uv: '0.13.7'
  });
});

const cli = (f: Fixture, args: string[], env: NodeJS.ProcessEnv = {}) =>
  spawnSync(
    process.execPath,
    [path.join(root, 'scripts/workspace.ts'), ...args],
    {
      cwd: f.scratch,
      env: { ...process.env, BPP_CONFIG_HOME: f.home, ...env },
      encoding: 'utf8'
    }
  );

test('CLI help lists every section and profile from the section table', (t) => {
  const f = fixture(t);
  const result = cli(f, ['--help']);
  assert.equal(result.status, 0, result.stderr);
  for (const name of [
    'server',
    'analyzer',
    'signing',
    'machine',
    'game',
    'cloudflare',
    'qqbot'
  ])
    assert.match(result.stdout, new RegExp(`\\[${name}\\]`));
  assert.match(
    result.stdout,
    /Profiles: server, analyzer, signing, mod, game, release, qqbot\./
  );
  assert.match(result.stdout, /secrets push \[--dependabot\]$/m);
});

test('CLI preserves cwd, literal arguments, child status and keeps config values out of output', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.toml',
    tomlConfig({ cloudflare: 'CLOUDFLARE_API_TOKEN=DO-NOT-PRINT\n' })
  );
  const script = f.put(
    f.scratch,
    'child.mjs',
    `import assert from 'node:assert/strict';
assert.equal(process.env.CLOUDFLARE_API_TOKEN, 'DO-NOT-PRINT');
assert.equal(process.argv[2], '$(touch BAD); argument with spaces');
assert.equal(process.cwd(), ${JSON.stringify(f.scratch)});
process.exit(37);
`
  );
  const result = cli(
    f,
    [
      'run',
      'release',
      '--',
      process.execPath,
      script,
      '$(touch BAD); argument with spaces'
    ],
    { CLOUDFLARE_API_TOKEN: '' }
  );
  assert.equal(result.status, 37, result.stdout + result.stderr);
  assert.doesNotMatch(result.stdout + result.stderr, /DO-NOT-PRINT/);
  assert.equal(fs.existsSync(path.join(f.scratch, 'BAD')), false);
});

test('CLI signing run exposes only a staging path and removes it even when the command fails', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.toml',
    tomlConfig({
      signing:
        'APPLE_API_ISSUER=DO-NOT-PRINT\nAPPLE_CERTIFICATE_PASSWORD=DO-NOT-PRINT\n'
    })
  );
  const script = f.put(
    f.scratch,
    'child.mjs',
    `import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
const directory = process.env.BPP_SIGNING_SECRETS_DIR;
assert.ok(!process.env.APPLE_API_ISSUER);
assert.ok(!process.env.APPLE_CERTIFICATE_PASSWORD);
assert.equal(fs.readFileSync(path.join(directory, 'apple-api-issuer'), 'utf8'), 'DO-NOT-PRINT');
assert.equal(fs.existsSync(path.join(directory, 'apple-certificate-password')), false);
process.exit(9);
`
  );
  const result = cli(f, ['run', 'signing', '--', process.execPath, script], {
    BPP_SIGNING_SECRETS_DIR: '',
    APPLE_API_ISSUER: '',
    APPLE_CERTIFICATE_PASSWORD: ''
  });
  assert.equal(result.status, 9, result.stdout + result.stderr);
  assert.doesNotMatch(result.stdout + result.stderr, /DO-NOT-PRINT/);
  assert.deepEqual(
    fs.readdirSync(f.home).filter((entry) => entry.startsWith('.signing-')),
    []
  );
});

test('CLI refuses an unknown profile naming the profiles the table defines', (t) => {
  const f = fixture(t);
  const result = cli(f, ['run', 'bogus', '--', process.execPath, '-e', '']);
  assert.equal(result.status, 1);
  assert.match(
    result.stderr,
    /Unknown profile; use server, analyzer, signing, mod, game, release or qqbot/
  );
});

// A gh stand-in on PATH: lists come from a JSON state file keyed by scope,
// every call is recorded with its stdin, and nothing is ever printed back.
// One recorded gh call: its arguments and what it read from stdin.
interface GhCall {
  args: string[];
  stdin: string;
}

function ghStub(f: Fixture) {
  const bin = path.join(f.directory, 'bin');
  fs.mkdirSync(bin, { recursive: true });
  const stateFile = path.join(f.directory, 'gh-state.json');
  fs.writeFileSync(stateFile, '{}');
  const log = path.join(f.directory, 'gh-calls.jsonl');
  const script = path.join(f.directory, 'gh-stub.mjs');
  fs.writeFileSync(
    script,
    `import fs from 'node:fs';
const args = process.argv.slice(2);
const stdin = process.stdin.isTTY ? '' : fs.readFileSync(0, 'utf8');
fs.appendFileSync(${JSON.stringify(log)}, JSON.stringify({ args, stdin }) + '\\n');
const state = JSON.parse(fs.readFileSync(${JSON.stringify(stateFile)}, 'utf8'));
const flag = (name) => { const i = args.indexOf(name); return i === -1 ? null : args[i + 1]; };
const key = [args[0], flag('--env') ?? flag('--app') ?? 'repository'].join(':');
if (args[1] === 'list') {
  process.stdout.write(JSON.stringify((state[key] ?? []).map((name) => ({ name }))));
} else if (args[1] === 'set') {
  state[key] = [...new Set([...(state[key] ?? []), args[2]])];
  fs.writeFileSync(${JSON.stringify(stateFile)}, JSON.stringify(state));
  process.stdout.write('set ' + args[2] + '\\n');
}
`
  );
  fs.writeFileSync(
    path.join(bin, 'gh'),
    `#!/usr/bin/env bash\nexec '${process.execPath}' '${script}' "$@"\n`,
    { mode: 0o755 }
  );
  return {
    bin,
    calls: () =>
      fs.existsSync(log)
        ? fs
            .readFileSync(log, 'utf8')
            .trim()
            .split('\n')
            .map((line): GhCall => JSON.parse(line))
        : [],
    state: () => JSON.parse(fs.readFileSync(stateFile, 'utf8'))
  };
}

const secretsConfig = tomlConfig({
  server: 'BUNDLE_SYNC_TOKEN=sync\n',
  signing:
    'APPLE_SIGNING_IDENTITY=Developer ID Application: X\nAPPLE_API_ISSUER=issuer-id\nAPPLE_API_KEY=K1\nAPPLE_CERTIFICATE_PASSWORD=\n',
  cloudflare:
    'CLOUDFLARE_API_TOKEN=operator-token\nCLOUDFLARE_ACCOUNT_ID=cccccccccccccccccccccccccccccccc\n'
});

test('secrets check names what GitHub and the local configuration hold, never a value', (t) => {
  const f = fixture(t);
  f.put(f.home, 'config.toml', secretsConfig);
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER-KEY');
  f.put(f.home, 'keys/AuthKey_K1.p8', 'P8-KEY');
  const gh = ghStub(f);
  const result = cli(f, ['secrets', 'check'], {
    PATH: `${gh.bin}${path.delimiter}${process.env.PATH}`
  });
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.equal(
    result.stdout,
    `CLOUDFLARE_ACCOUNT_ID       repository variable           GitHub MISSING  local present (config.toml [cloudflare] CLOUDFLARE_ACCOUNT_ID)
CLOUDFLARE_API_TOKEN        repository secret             GitHub MISSING  local present (config.toml [cloudflare] CLOUDFLARE_API_TOKEN)
BPP_BUNDLE_SYNC_TOKEN       analyzer environment secret   GitHub MISSING  local present (config.toml [server] BUNDLE_SYNC_TOKEN)
STEAM_SNAPSHOT_LOGIN        steam environment secret      GitHub MISSING  local MISSING (contents of keys/steam-snapshot-login.json)
STEAM_BETA_PASSWORDS        steam environment secret      GitHub MISSING  local MISSING (contents of keys/steam-beta-passwords.json)
QQ_BOT_APP_ID               qqbot environment secret      GitHub MISSING  local MISSING (config.toml [qqbot] QQ_BOT_APP_ID)
QQ_BOT_APP_SECRET           qqbot environment secret      GitHub MISSING  local MISSING (config.toml [qqbot] QQ_BOT_APP_SECRET)
TAURI_SIGNING_PRIVATE_KEY   release environment secret    GitHub MISSING  local present (contents of keys/tauri-updater.key)
ANDROID_KEYSTORE            release environment secret    GitHub MISSING  local MISSING (base64 of keys/android-release.p12)
ANDROID_KEYSTORE_PASSWORD   release environment secret    GitHub MISSING  local MISSING (config.toml [signing] ANDROID_KEYSTORE_PASSWORD)
ANDROID_KEY_ALIAS           release environment secret    GitHub MISSING  local MISSING (config.toml [signing] ANDROID_KEY_ALIAS)
APPLE_SIGNING_IDENTITY      release environment variable  GitHub MISSING  local present (config.toml [signing] APPLE_SIGNING_IDENTITY)
APPLE_API_ISSUER            release environment variable  GitHub MISSING  local present (config.toml [signing] APPLE_API_ISSUER)
APPLE_API_KEY               release environment variable  GitHub MISSING  local present (config.toml [signing] APPLE_API_KEY)
APPLE_API_KEY_P8            release environment secret    GitHub MISSING  local present (contents of keys/AuthKey_<APPLE_API_KEY>.p8)
APPLE_CERTIFICATE           release environment secret    GitHub MISSING  local MISSING (base64 of keys/developer-id.p12)
APPLE_CERTIFICATE_PASSWORD  release environment secret    GitHub MISSING  local MISSING (config.toml [signing] APPLE_CERTIFICATE_PASSWORD)
17 managed names: 17 missing on GitHub, 9 missing locally.
Dependabot secrets were not listed; pass --dependabot to include them.
`
  );
  for (const value of [
    'operator-token',
    'cccccccccccccccccccccccccccccccc',
    'UPDATER-KEY',
    'P8-KEY',
    'issuer-id'
  ])
    assert.doesNotMatch(result.stdout + result.stderr, new RegExp(value));
  assert.ok(gh.calls().every(({ args }) => args[1] === 'list'));
  assert.ok(gh.calls().every(({ stdin }) => stdin === ''));
});

test('secrets push copies each local value over stdin to its scope and skips absent ones', (t) => {
  const f = fixture(t);
  const config = parseConfig(secretsConfig);
  Object.assign(config.sections.get('signing')!.values, {
    ANDROID_KEYSTORE_PASSWORD: 'ks-pass',
    ANDROID_KEY_ALIAS: 'bpp'
  });
  config.sections.set('qqbot', {
    values: { QQ_BOT_APP_ID: 'app-id', QQ_BOT_APP_SECRET: 'APP-SECRET' }
  });
  f.put(f.home, 'config.toml', renderConfig(config));
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER-KEY\n');
  f.put(f.home, 'keys/AuthKey_K1.p8', 'P8-KEY');
  f.put(f.home, 'keys/developer-id.p12', Buffer.from([0x30, 0x82, 0x00, 0xff]));
  f.put(f.home, 'keys/android-release.p12', Buffer.from([0x30, 0x82, 0x0a]));
  f.put(f.home, 'keys/steam-snapshot-login.json', 'STEAM-LOGIN');
  f.put(f.home, 'keys/steam-beta-passwords.json', 'STEAM-CODES');
  const gh = ghStub(f);
  const env = { PATH: `${gh.bin}${path.delimiter}${process.env.PATH}` };
  let result = cli(f, ['secrets', 'push', '--dependabot'], env);
  assert.equal(result.status, 1, result.stdout + result.stderr);
  assert.doesNotMatch(
    result.stdout + result.stderr,
    /operator-token|UPDATER-KEY|P8-KEY|MIIA|issuer-id|ks-pass|STEAM-LOGIN|STEAM-CODES|APP-SECRET/
  );
  assert.match(
    result.stdout,
    /^set repository variable CLOUDFLARE_ACCOUNT_ID$/m
  );
  assert.match(
    result.stdout,
    /^set release environment secret APPLE_CERTIFICATE$/m
  );
  assert.match(
    result.stdout,
    /^skipped release environment secret APPLE_CERTIFICATE_PASSWORD: no local value/m
  );
  assert.match(
    result.stdout,
    /^Set 16 GitHub names; 1 without a local value\.$/m
  );
  const sets = gh.calls().filter(({ args }) => args[1] === 'set');
  const byName = Object.fromEntries(
    sets.map(({ args, stdin }) => [
      `${args[0]}:${args[2]}:${args.slice(3).join(' ')}`,
      stdin
    ])
  );
  assert.deepEqual(byName, {
    'variable:CLOUDFLARE_ACCOUNT_ID:': 'cccccccccccccccccccccccccccccccc',
    'secret:CLOUDFLARE_API_TOKEN:': 'operator-token',
    'secret:CLOUDFLARE_API_TOKEN:--app dependabot': 'operator-token',
    'secret:BPP_BUNDLE_SYNC_TOKEN:--env analyzer': 'sync',
    'secret:STEAM_SNAPSHOT_LOGIN:--env steam': 'STEAM-LOGIN',
    'secret:STEAM_BETA_PASSWORDS:--env steam': 'STEAM-CODES',
    'secret:QQ_BOT_APP_ID:--env qqbot': 'app-id',
    'secret:QQ_BOT_APP_SECRET:--env qqbot': 'APP-SECRET',
    'secret:TAURI_SIGNING_PRIVATE_KEY:--env release': 'UPDATER-KEY\n',
    'secret:ANDROID_KEYSTORE:--env release': Buffer.from([
      0x30, 0x82, 0x0a
    ]).toString('base64'),
    'secret:ANDROID_KEYSTORE_PASSWORD:--env release': 'ks-pass',
    'secret:ANDROID_KEY_ALIAS:--env release': 'bpp',
    'variable:APPLE_SIGNING_IDENTITY:--env release':
      'Developer ID Application: X',
    'variable:APPLE_API_ISSUER:--env release': 'issuer-id',
    'variable:APPLE_API_KEY:--env release': 'K1',
    'secret:APPLE_API_KEY_P8:--env release': 'P8-KEY',
    'secret:APPLE_CERTIFICATE:--env release': Buffer.from([
      0x30, 0x82, 0x00, 0xff
    ]).toString('base64')
  });
});

test('secrets refuses unknown arguments before calling gh', (t) => {
  const f = fixture(t);
  f.put(f.home, 'config.toml', secretsConfig);
  const gh = ghStub(f);
  for (const args of [
    ['secrets'],
    ['secrets', 'list'],
    ['secrets', 'check', '--prune'],
    ['secrets', 'push', '--force']
  ]) {
    const result = cli(f, args, {
      PATH: `${gh.bin}${path.delimiter}${process.env.PATH}`
    });
    assert.equal(result.status, 1, args.join(' '));
  }
  assert.deepEqual(gh.calls(), []);
});
