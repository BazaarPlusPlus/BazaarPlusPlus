import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import {
  root,
  initializeConfig,
  refreshProjections,
  withProfileEnvironment,
  readConfig,
  parseConfig,
  projectionChanges,
  assertExternal
} from './local-config.mjs';
import { doctor } from './workspace.mjs';

function fixture(t) {
  const directory = fs.realpathSync(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp setup '))
  );
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
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
  const put = (base, file, text) => {
    const target = path.join(base, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
    return target;
  };
  return { directory, repository, scratch, home, put };
}

const ini = (sections) =>
  Object.entries(sections)
    .map(([name, body]) => `[${name}]\n${body}`)
    .join('\n');

const projectedMarker =
  '# Projected from config.ini [server] and [cloudflare]; edit there.';

const noEcho = (pattern) => (error) => {
  assert.match(error.message, pattern);
  assert.doesNotMatch(error.message, /SECRET/);
  return true;
};

test('the template built from the real analyzer .env.example parses and lists every section key once', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, root);
  const text = fs.readFileSync(path.join(f.home, 'config.ini'), 'utf8');
  const config = readConfig(f.home);
  assert.deepEqual(
    [...config.sections.keys()],
    ['server', 'analyzer', 'signing', 'machine', 'cloudflare']
  );
  assert.ok('BPP_DATA_ROOT' in config.sections.get('analyzer').values);
  assert.ok('BPP_METRICS_R2_BUCKET' in config.sections.get('analyzer').values);
  for (const key of [
    'BAZAARDB_DELIVERY_TOKEN',
    'APPLE_CERTIFICATE_PASSWORD',
    'BPP_GAME_ROOT',
    'CLOUDFLARE_API_TOKEN',
    'CLOUDFLARE_ACCOUNT_ID',
    'BPP_GAME_LIBS_TOKEN'
  ])
    assert.equal(text.match(new RegExp(`^${key}=$`, 'gm'))?.length, 1, key);
});

test('setup preserves config.ini, tightens POSIX permissions and removes only stale signing staging', (t) => {
  const f = fixture(t);
  const text = ini({ cloudflare: 'CLOUDFLARE_API_TOKEN=KEEP\n' });
  const file = f.put(f.home, 'config.ini', text);
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

test('config.ini rejects ambiguous text by line without echoing values', () => {
  for (const [text, pattern] of [
    [
      '[server] # note\nA=SECRET\n',
      /Malformed section header on config.ini line 1/
    ],
    ['[Server]\nA=SECRET\n', /Unknown section header on config.ini line 1/],
    ['[server]\nA=1\n[server]\n', /Duplicate \[server\] on config.ini line 3/],
    [
      '[server]\nSECRET-TEXT\n',
      /Expected KEY=value inside a section on config.ini line 2/
    ],
    [
      'A=SECRET\n[server]\n',
      /Expected KEY=value inside a section on config.ini line 1/
    ],
    [
      '[server]\nA="SECRET\n[analyzer]\nB"\n',
      /Unterminated quote for A on config.ini line 2/
    ],
    [
      '[server]\nA="abc" xSECRET"\n',
      /Unexpected text after the closing quote for A on config.ini line 2/
    ],
    ['[server]\nexport A=1\nA=SECRET\n', /Duplicate A on config.ini line 3/],
    ['[server]\n; SECRET\n', /Use # for comments on config.ini line 2/],
    ['[server]\nA=`SECRET`\n', /Backtick quotes .* A on config.ini line 2/],
    [
      '[server]\nA=SECRET#x\n',
      /Unquoted # in the value for A on config.ini line 2/
    ],
    ['[server]\nA= #SECRET\n', /Value for A on config.ini line 2 starts/],
    ['[server]\nA=\t#SECRET\n', /Value for A on config.ini line 2 starts/],
    [
      '[machine]\nBPP_GAME_ROOT="D:\\new SECRET\\x"\n',
      /Backslash in a double-quoted value for BPP_GAME_ROOT on config.ini line 2; use single quotes or leave it unquoted/
    ],
    [
      "[machine]\nBPP_GAME_ROOT='\\\\host\\SECRET'\n",
      /Escaped backslash in a single-quoted value for BPP_GAME_ROOT on config.ini line 2/
    ],
    ['[server]\nA=SEC\rRET\n', /Carriage return or NUL on config.ini line 2/]
  ]) {
    assert.throws(() => parseConfig(text), noEcho(pattern), text);
  }
});

test('config.ini accepts indentation, BOM, CRLF, export, comments and literal Windows paths', () => {
  const config = parseConfig(
    '\uFEFF# head\r\n\r\n  [server]  \r\nexport A = "x # y"\r\n# kept\r\n\r\n[analyzer]\r\n'
  );
  assert.equal(config.preamble, '# head\n');
  assert.equal(
    config.sections.get('server').body,
    'export A = "x # y"\n# kept\n'
  );
  assert.deepEqual(config.sections.get('server').values, { A: 'x # y' });
  assert.equal(config.sections.get('analyzer').body, '');
  const machine = parseConfig(
    [
      '[machine]',
      'BPP_GAME_ROOT = D:\\new games\\The Bazaar  # note',
      "BPP_MANAGED_PATH='D:\\new games\\Managed' # note",
      'B=v\t# tab comment',
      'C=',
      'D="a`b\'c"#tight',
      '  E  =  spaced value  '
    ].join('\n')
  ).sections.get('machine').values;
  assert.deepEqual(machine, {
    BPP_GAME_ROOT: 'D:\\new games\\The Bazaar',
    BPP_MANAGED_PATH: 'D:\\new games\\Managed',
    B: 'v',
    C: '',
    D: "a`b'c",
    E: 'spaced value'
  });
});

test('central edits refresh before use; local edits cause conflict before any projection is replaced', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, f.repository);
  refreshProjections(f.home, f.repository);
  const analyzerEnv = path.join(f.repository, 'bazaarplusplus-analyzer/.env');
  const projected = (sync, token, account) =>
    `${projectedMarker}\nBPP_BUNDLE_SYNC_TOKEN="${sync}"\nCLOUDFLARE_API_TOKEN="${token}"\nCLOUDFLARE_ACCOUNT_ID="${account}"\n`;
  f.put(
    f.home,
    'config.ini',
    ini({
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
    `BPP_DATA_ROOT=/changed\n${projected('shared', 'cf-token', '')}`
  );
  f.put(
    f.home,
    'config.ini',
    ini({ server: '', analyzer: 'BPP_DATA_ROOT=/next\n' })
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
    `BPP_DATA_ROOT=/changed\n${projected('shared', 'cf-token', '')}`
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
    'config.ini',
    ini({
      cloudflare:
        'CLOUDFLARE_ACCOUNT_ID=account\nCLOUDFLARE_API_TOKEN="$(touch never); `exit 1`"\nBPP_GAME_LIBS_TOKEN=read-only\nNODE_OPTIONS=--bad\n',
      machine: 'CLOUDFLARE_ACCOUNT_ID=wrong-section\n'
    })
  );
  const env = (profile, given = {}) =>
    withProfileEnvironment(profile, f.home, (env) => env, given, f.repository);
  assert.deepEqual(env('release', { CLOUDFLARE_ACCOUNT_ID: 'explicit' }), {
    CLOUDFLARE_ACCOUNT_ID: 'explicit',
    CLOUDFLARE_API_TOKEN: '$(touch never); `exit 1`'
  });
  assert.deepEqual(env('mod'), {});
  assert.throws(
    () => env('cloudflare'),
    /Unknown profile; use server, analyzer, signing, mod or release/
  );
});

test('missing canonical section cannot silently run with a stale checkout file', (t) => {
  const f = fixture(t);
  f.put(f.repository, 'bazaarplusplus-analyzer/.env', 'BPP_DATA_ROOT=/old\n');
  assert.throws(
    () =>
      withProfileEnvironment('analyzer', f.home, () => {}, {}, f.repository),
    /Missing \[analyzer\] in config.ini/
  );
});

test('signing staging holds bundle.sh files only, never injects values, and is removed even when the command fails', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.ini',
    ini({
      signing:
        "APPLE_API_KEY=K1\nAPPLE_API_ISSUER=issuer\nAPPLE_API_KEY_PATH=keys/Other.p8\nAPPLE_CERTIFICATE_PASSWORD='p\\w'\nANDROID_KEYSTORE_PASSWORD=ks-pass\nANDROID_KEY_ALIAS=bpp\n"
    })
  );
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER');
  f.put(f.home, 'keys/developer-id.p12', 'CERT');
  f.put(f.home, 'keys/android-release.p12', 'KEYSTORE');
  f.put(f.home, 'keys/notes.txt', 'NOT-KEY-MATERIAL');
  let directory;
  const env = withProfileEnvironment(
    'signing',
    f.home,
    (env) => {
      directory = env.BPP_SIGNING_SECRETS_DIR;
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
  assert.equal(fs.existsSync(directory), false);
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
  assert.equal(fs.existsSync(directory), false);
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
    'config.ini',
    ini({
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
  const row = (scope) => rows.filter((row) => row.scope === scope);
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

const cli = (f, args, env = {}) =>
  spawnSync(
    process.execPath,
    [path.join(root, 'scripts/workspace.mjs'), ...args],
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
  for (const name of ['server', 'analyzer', 'signing', 'machine', 'cloudflare'])
    assert.match(result.stdout, new RegExp(`\\[${name}\\]`));
  assert.match(
    result.stdout,
    /Profiles: server, analyzer, signing, mod, release\./
  );
  assert.match(result.stdout, /secrets push \[--dependabot\]$/m);
});

test('CLI preserves cwd, literal arguments, child status and keeps config values out of output', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.ini',
    ini({ cloudflare: 'CLOUDFLARE_API_TOKEN=DO-NOT-PRINT\n' })
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
    'config.ini',
    ini({
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
    /Unknown profile; use server, analyzer, signing, mod or release/
  );
});

// A gh stand-in on PATH: lists come from a JSON state file keyed by scope,
// every call is recorded with its stdin, and nothing is ever printed back.
function ghStub(f) {
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
        ? fs.readFileSync(log, 'utf8').trim().split('\n').map(JSON.parse)
        : [],
    state: () => JSON.parse(fs.readFileSync(stateFile, 'utf8'))
  };
}

const secretsConfig = ini({
  server: 'BUNDLE_SYNC_TOKEN=sync\n',
  signing:
    'APPLE_SIGNING_IDENTITY=Developer ID Application: X\nAPPLE_API_ISSUER=issuer-id\nAPPLE_API_KEY=K1\nAPPLE_CERTIFICATE_PASSWORD=\n',
  cloudflare:
    'CLOUDFLARE_API_TOKEN=operator-token\nCLOUDFLARE_ACCOUNT_ID=cccccccccccccccccccccccccccccccc\nBPP_GAME_LIBS_TOKEN=\n'
});

test('secrets check names what GitHub and the local configuration hold, never a value', (t) => {
  const f = fixture(t);
  f.put(f.home, 'config.ini', secretsConfig);
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER-KEY');
  f.put(f.home, 'keys/AuthKey_K1.p8', 'P8-KEY');
  const gh = ghStub(f);
  const result = cli(f, ['secrets', 'check'], {
    PATH: `${gh.bin}${path.delimiter}${process.env.PATH}`
  });
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.equal(
    result.stdout,
    `CLOUDFLARE_ACCOUNT_ID       repository variable           GitHub MISSING  local present (config.ini [cloudflare] CLOUDFLARE_ACCOUNT_ID)
BPP_GAME_LIBS_TOKEN         repository secret             GitHub MISSING  local MISSING (config.ini [cloudflare] BPP_GAME_LIBS_TOKEN)
CLOUDFLARE_API_TOKEN        repository secret             GitHub MISSING  local present (config.ini [cloudflare] CLOUDFLARE_API_TOKEN)
BPP_BUNDLE_SYNC_TOKEN       analyzer environment secret   GitHub MISSING  local present (config.ini [server] BUNDLE_SYNC_TOKEN)
TAURI_SIGNING_PRIVATE_KEY   release environment secret    GitHub MISSING  local present (contents of keys/tauri-updater.key)
ANDROID_KEYSTORE            release environment secret    GitHub MISSING  local MISSING (base64 of keys/android-release.p12)
ANDROID_KEYSTORE_PASSWORD   release environment secret    GitHub MISSING  local MISSING (config.ini [signing] ANDROID_KEYSTORE_PASSWORD)
ANDROID_KEY_ALIAS           release environment secret    GitHub MISSING  local MISSING (config.ini [signing] ANDROID_KEY_ALIAS)
APPLE_SIGNING_IDENTITY      release environment variable  GitHub MISSING  local present (config.ini [signing] APPLE_SIGNING_IDENTITY)
APPLE_API_ISSUER            release environment variable  GitHub MISSING  local present (config.ini [signing] APPLE_API_ISSUER)
APPLE_API_KEY               release environment variable  GitHub MISSING  local present (config.ini [signing] APPLE_API_KEY)
APPLE_API_KEY_P8            release environment secret    GitHub MISSING  local present (contents of keys/AuthKey_<APPLE_API_KEY>.p8)
APPLE_CERTIFICATE           release environment secret    GitHub MISSING  local MISSING (base64 of keys/developer-id.p12)
APPLE_CERTIFICATE_PASSWORD  release environment secret    GitHub MISSING  local MISSING (config.ini [signing] APPLE_CERTIFICATE_PASSWORD)
14 managed names: 14 missing on GitHub, 6 missing locally.
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
  f.put(
    f.home,
    'config.ini',
    secretsConfig.replace(
      'APPLE_CERTIFICATE_PASSWORD=\n',
      'APPLE_CERTIFICATE_PASSWORD=\nANDROID_KEYSTORE_PASSWORD=ks-pass\nANDROID_KEY_ALIAS=bpp\n'
    )
  );
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER-KEY\n');
  f.put(f.home, 'keys/AuthKey_K1.p8', 'P8-KEY');
  f.put(f.home, 'keys/developer-id.p12', Buffer.from([0x30, 0x82, 0x00, 0xff]));
  f.put(f.home, 'keys/android-release.p12', Buffer.from([0x30, 0x82, 0x0a]));
  const gh = ghStub(f);
  const env = { PATH: `${gh.bin}${path.delimiter}${process.env.PATH}` };
  let result = cli(f, ['secrets', 'push', '--dependabot'], env);
  assert.equal(result.status, 1, result.stdout + result.stderr);
  assert.doesNotMatch(
    result.stdout + result.stderr,
    /operator-token|UPDATER-KEY|P8-KEY|MIIA|issuer-id|ks-pass/
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
    /^skipped repository secret BPP_GAME_LIBS_TOKEN: no local value \(config\.ini \[cloudflare\] BPP_GAME_LIBS_TOKEN\)$/m
  );
  assert.match(
    result.stdout,
    /^skipped release environment secret APPLE_CERTIFICATE_PASSWORD: no local value/m
  );
  assert.match(
    result.stdout,
    /^Set 12 GitHub names; 2 without a local value\.$/m
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
    'secret:BPP_BUNDLE_SYNC_TOKEN:--env analyzer': 'sync',
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
  // The read-only token, once present, also reaches Dependabot with --dependabot.
  f.put(
    f.home,
    'config.ini',
    secretsConfig.replace(
      'BPP_GAME_LIBS_TOKEN=\n',
      'BPP_GAME_LIBS_TOKEN=read-only\n'
    )
  );
  result = cli(f, ['secrets', 'push', '--dependabot'], env);
  assert.equal(result.status, 1, result.stdout + result.stderr);
  assert.doesNotMatch(result.stdout + result.stderr, /read-only/);
  const dependabot = gh
    .calls()
    .filter(({ args }) => args.includes('dependabot') && args[1] === 'set');
  assert.deepEqual(
    dependabot.map(({ args, stdin }) => [args.slice(0, 3).join(' '), stdin]),
    [['secret set BPP_GAME_LIBS_TOKEN', 'read-only']]
  );
  assert.ok(gh.state()['secret:repository'].includes('BPP_GAME_LIBS_TOKEN'));
});

test('secrets refuses unknown arguments before calling gh', (t) => {
  const f = fixture(t);
  f.put(f.home, 'config.ini', secretsConfig);
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
