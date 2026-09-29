import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import {
  root,
  importCheckout,
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
  const repository = path.join(directory, 'new checkout');
  const source = path.join(directory, 'old checkout');
  const home = path.join(directory, 'config');
  for (const checkout of [repository, source]) {
    for (const project of ['analyzer', 'server', 'installer']) {
      fs.mkdirSync(path.join(checkout, `bazaarplusplus-${project}`), {
        recursive: true
      });
    }
    fs.writeFileSync(
      path.join(checkout, 'bazaarplusplus-analyzer/.env.example'),
      'BPP_DATA_ROOT=/absolute/path/to/data\n'
    );
  }
  const put = (base, file, text) => {
    const target = path.join(base, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, text);
    return target;
  };
  const values = (section) =>
    readConfig(home).sections.get(section)?.values || {};
  return { directory, repository, source, home, put, values };
}

const ini = (sections) =>
  Object.entries(sections)
    .map(([name, body]) => `[${name}]\n${body}`)
    .join('\n');

const signingDir = 'bazaarplusplus-installer/signing-secrets';

const noEcho = (pattern) => (error) => {
  assert.match(error.message, pattern);
  assert.doesNotMatch(error.message, /SECRET/);
  return true;
};

test('import after setup fills config.ini, keeps data and Apple key meaning, protects permissions, and is repeatable', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, f.repository);
  f.put(
    f.source,
    'bazaarplusplus-analyzer/.env',
    'BPP_DATA_ROOT=../durable-state\nBPP_BUNDLE_SYNC_TOKEN=secret-value\n'
  );
  f.put(
    f.source,
    'bazaarplusplus-server/.dev.vars',
    'BUNDLE_SYNC_TOKEN=secret-value\n'
  );
  f.put(f.source, `${signingDir}/apple-api-key`, 'TEST\n');
  f.put(f.source, `${signingDir}/apple-signing-identity`, 'Dev ID: A (#1)\n');
  f.put(
    f.source,
    `${signingDir}/apple-api-key-path`,
    'signing-secrets/AuthKey_TEST.p8\n'
  );
  f.put(f.source, `${signingDir}/AuthKey_TEST.p8`, 'PRIVATE-TEST-KEY');
  f.put(f.source, `${signingDir}/tauri-updater.key`, 'UPDATER-KEY');
  const imported = importCheckout(f.source, f.home, f.repository);
  assert.deepEqual(imported, [
    '[server]',
    '[analyzer]',
    '[signing] APPLE_SIGNING_IDENTITY',
    '[signing] APPLE_API_KEY',
    'keys/AuthKey_TEST.p8',
    'keys/tauri-updater.key'
  ]);
  const before = fs.readFileSync(path.join(f.home, 'config.ini'));
  // A value without a backslash keeps the double-quoted form earlier
  // imports wrote, so their files stay byte-identical.
  assert.match(before.toString(), /^APPLE_API_KEY="TEST"$/m);
  importCheckout(f.source, f.home, f.repository);
  assert.deepEqual(fs.readFileSync(path.join(f.home, 'config.ini')), before);
  assert.equal(
    f.values('analyzer').BPP_DATA_ROOT,
    path.join(f.source, 'durable-state').replaceAll('\\', '/')
  );
  assert.equal(fs.existsSync(path.join(f.source, 'durable-state')), false);
  assert.equal(f.values('server').BUNDLE_SYNC_TOKEN, 'secret-value');
  assert.deepEqual(f.values('signing'), {
    APPLE_SIGNING_IDENTITY: 'Dev ID: A (#1)',
    APPLE_API_ISSUER: '',
    APPLE_API_KEY: 'TEST',
    APPLE_API_KEY_PATH: '',
    TAURI_SIGNING_PRIVATE_KEY_PASSWORD: ''
  });
  assert.equal(
    fs.readFileSync(path.join(f.home, 'keys/AuthKey_TEST.p8'), 'utf8'),
    'PRIVATE-TEST-KEY'
  );
  assert.equal(
    fs.readFileSync(
      path.join(f.source, `${signingDir}/apple-api-key-path`),
      'utf8'
    ),
    'signing-secrets/AuthKey_TEST.p8\n'
  );
  if (process.platform !== 'win32') {
    assert.equal(fs.statSync(f.home).mode & 0o777, 0o700);
    assert.equal(fs.statSync(path.join(f.home, 'keys')).mode & 0o777, 0o700);
    for (const file of ['config.ini', 'keys/AuthKey_TEST.p8'])
      assert.equal(fs.statSync(path.join(f.home, file)).mode & 0o777, 0o600);
  }
});

test('an imported value with backslashes is written in single quotes and reaches signing staging literally', (t) => {
  const f = fixture(t);
  const password = String.raw`p\new "quoted" \x`;
  f.put(f.source, `${signingDir}/tauri-updater.password`, `${password}\n`);
  assert.deepEqual(importCheckout(f.source, f.home, f.repository), [
    '[signing] TAURI_SIGNING_PRIVATE_KEY_PASSWORD'
  ]);
  assert.match(
    fs.readFileSync(path.join(f.home, 'config.ini'), 'utf8'),
    /^TAURI_SIGNING_PRIVATE_KEY_PASSWORD='p\\new "quoted" \\x'$/m
  );
  assert.equal(
    f.values('signing').TAURI_SIGNING_PRIVATE_KEY_PASSWORD,
    password
  );
  const staged = withProfileEnvironment(
    'signing',
    f.home,
    (env) =>
      fs.readFileSync(
        path.join(env.BPP_SIGNING_SECRETS_DIR, 'tauri-updater.password'),
        'utf8'
      ),
    {},
    f.repository
  );
  assert.equal(staged, password);
  const before = fs.readFileSync(path.join(f.home, 'config.ini'));
  importCheckout(f.source, f.home, f.repository);
  assert.deepEqual(fs.readFileSync(path.join(f.home, 'config.ini')), before);
});

test('a value no quoting keeps literal is refused by name before any write', (t) => {
  for (const password of [
    String.raw`it's\SECRET`,
    String.raw`SECRET\\share`,
    'SECRET\\',
    'SECRET\\\rmore'
  ]) {
    const f = fixture(t);
    f.put(f.source, `${signingDir}/tauri-updater.password`, password);
    assert.throws(
      () => importCheckout(f.source, f.home, f.repository),
      noEcho(/Unsupported characters for TAURI_SIGNING_PRIVATE_KEY_PASSWORD/)
    );
    assert.equal(fs.existsSync(f.home), false);
  }
});

test('an old checkout file is parsed with config.ini rules and its errors name that file', (t) => {
  for (const [text, pattern] of [
    [
      'BPP_DATA_ROOT=../first\nBPP_DATA_ROOT=../SECRET\n',
      /Duplicate BPP_DATA_ROOT on .*bazaarplusplus-analyzer[\\/]\.env line 2/
    ],
    [
      '# note\nBPP_DATA_ROOT="C:\\SECRET"\n',
      /Backslash in a double-quoted value for BPP_DATA_ROOT on .*bazaarplusplus-analyzer[\\/]\.env line 2/
    ]
  ]) {
    const f = fixture(t);
    f.put(f.source, 'bazaarplusplus-analyzer/.env', text);
    assert.throws(
      () => importCheckout(f.source, f.home, f.repository),
      noEcho(pattern)
    );
    assert.equal(fs.existsSync(f.home), false);
  }
});

test('an import conflict changes neither existing credentials nor other files', (t) => {
  const f = fixture(t);
  f.put(f.source, 'bazaarplusplus-analyzer/.env', 'BPP_DATA_ROOT=/old\n');
  f.put(
    f.source,
    'bazaarplusplus-server/.dev.vars',
    'BUNDLE_SYNC_TOKEN=NEW-SECRET\n'
  );
  f.put(f.source, `${signingDir}/tauri-updater.key`, 'KEY');
  const existing = ini({ server: 'BUNDLE_SYNC_TOKEN=EXISTING-SECRET\n' });
  f.put(f.home, 'config.ini', existing);
  assert.throws(
    () => importCheckout(f.source, f.home, f.repository),
    noEcho(/Import conflict: \[server\]/)
  );
  assert.equal(
    fs.readFileSync(path.join(f.home, 'config.ini'), 'utf8'),
    existing
  );
  assert.equal(fs.existsSync(path.join(f.home, 'keys')), false);
});

test('an unknown signing-secrets entry is refused before any write', (t) => {
  const f = fixture(t);
  f.put(f.source, `${signingDir}/extra-token`, 'SECRET');
  assert.throws(
    () => importCheckout(f.source, f.home, f.repository),
    /Unknown signing-secrets entry: extra-token/
  );
  assert.equal(fs.existsSync(f.home), false);
});

test('the template built from the real analyzer .env.example parses and lists every section key once', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, root);
  const text = fs.readFileSync(path.join(f.home, 'config.ini'), 'utf8');
  const config = readConfig(f.home);
  assert.deepEqual(
    [...config.sections.keys()],
    ['server', 'analyzer', 'release', 'signing', 'machine', 'cloudflare']
  );
  assert.ok('BPP_DATA_ROOT' in config.sections.get('analyzer').values);
  for (const key of [
    'BAZAARDB_DELIVERY_TOKEN',
    'BPP_R2_SECRET_ACCESS_KEY',
    'TAURI_SIGNING_PRIVATE_KEY_PASSWORD',
    'BPP_GAME_ROOT',
    'CLOUDFLARE_ACCOUNT_ID'
  ])
    assert.equal(text.match(new RegExp(`^${key}=$`, 'gm'))?.length, 1, key);
});

test('setup preserves config.ini, tightens POSIX permissions and removes only stale signing staging', (t) => {
  const f = fixture(t);
  const text = ini({ release: 'BPP_R2_SECRET_ACCESS_KEY=KEEP\n' });
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

test('a legacy per-file layout is refused before projections are touched', (t) => {
  const f = fixture(t);
  f.put(f.home, 'server.dev.vars', 'BUNDLE_SYNC_TOKEN=LEGACY\n');
  f.put(
    f.repository,
    'bazaarplusplus-server/.dev.vars',
    'BUNDLE_SYNC_TOKEN=LEGACY\n'
  );
  for (const run of [
    () => initializeConfig(f.home, f.repository),
    () => withProfileEnvironment('server', f.home, () => {}, {}, f.repository)
  ])
    assert.throws(run, /Legacy configuration files .*server\.dev\.vars/);
  assert.equal(fs.existsSync(path.join(f.home, 'config.ini')), false);
  assert.equal(
    fs.readFileSync(
      path.join(f.repository, 'bazaarplusplus-server/.dev.vars'),
      'utf8'
    ),
    'BUNDLE_SYNC_TOKEN=LEGACY\n'
  );
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
  f.put(
    f.home,
    'config.ini',
    ini({ server: '', analyzer: 'BPP_DATA_ROOT=/changed\n' })
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
    'BPP_DATA_ROOT=/changed\n'
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
    'BPP_DATA_ROOT=/changed\n'
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
      release:
        'BPP_R2_ACCOUNT_ID=account\nBPP_R2_ACCESS_KEY_ID=file-key\nBPP_R2_SECRET_ACCESS_KEY="$(touch never); `exit 1`"\nNODE_OPTIONS=--bad\nCLOUDFLARE_API_TOKEN=unrelated\n',
      machine: 'BPP_R2_ACCOUNT_ID=wrong-section\n'
    })
  );
  const env = (profile, given = {}) =>
    withProfileEnvironment(profile, f.home, (env) => env, given, f.repository);
  assert.deepEqual(env('release', { BPP_R2_ACCESS_KEY_ID: 'explicit-key' }), {
    BPP_R2_ACCOUNT_ID: 'account',
    BPP_R2_ACCESS_KEY_ID: 'explicit-key',
    BPP_R2_SECRET_ACCESS_KEY: '$(touch never); `exit 1`'
  });
  assert.deepEqual(env('mod'), {});
  assert.throws(
    () => env('unknown'),
    /Unknown profile; use server, analyzer, release, signing, mod or cloudflare/
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
        "APPLE_API_KEY=K1\nAPPLE_API_ISSUER=issuer\nAPPLE_API_KEY_PATH=keys/Other.p8\nTAURI_SIGNING_PRIVATE_KEY_PASSWORD='p\\w'\n"
    })
  );
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER');
  f.put(f.home, 'keys/notes.txt', 'NOT-KEY-MATERIAL');
  let directory;
  const env = withProfileEnvironment(
    'signing',
    f.home,
    (env) => {
      directory = env.BPP_SIGNING_SECRETS_DIR;
      assert.deepEqual(fs.readdirSync(directory).sort(), [
        'apple-api-issuer',
        'apple-api-key',
        'apple-api-key-path',
        'tauri-updater.key',
        'tauri-updater.password'
      ]);
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
  const outside = f.put(f.source, 'untouched.env', 'KEEP');
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
      analyzer: 'BPP_DATA_ROOT=relative/data\n'
    })
  );
  f.put(f.repository, 'bazaarplusplus-analyzer/relative/data/.keep', '');
  const secrets = path.join(f.directory, 'explicit secrets');
  f.put(secrets, 'apple-api-issuer', 'issuer\n');
  f.put(secrets, 'apple-api-key', 'K1\n');
  f.put(secrets, 'apple-api-key-path', 'signing-secrets/AuthKey_K1.p8\n');
  f.put(secrets, 'tauri-updater.key', 'UPDATER');
  f.put(f.repository, `${signingDir}/AuthKey_K1.p8`, 'KEY');
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
  assert.equal(row('updater signing')[0].ok, true);
  if (process.platform === 'darwin')
    assert.equal(row('Apple notarization')[0].ok, true);
});

const cli = (f, args, env = {}) =>
  spawnSync(
    process.execPath,
    [path.join(root, 'scripts/workspace.mjs'), ...args],
    {
      cwd: f.source,
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
    'release',
    'signing',
    'machine',
    'cloudflare'
  ])
    assert.match(result.stdout, new RegExp(`\\[${name}\\]`));
  assert.match(
    result.stdout,
    /Profiles: server, analyzer, release, signing, mod, cloudflare\./
  );
});

test('CLI preserves cwd, literal arguments, child status and keeps config values out of output', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.ini',
    ini({ release: 'BPP_R2_SECRET_ACCESS_KEY=DO-NOT-PRINT\n' })
  );
  const script = f.put(
    f.source,
    'child.mjs',
    `import assert from 'node:assert/strict';
assert.equal(process.env.BPP_R2_SECRET_ACCESS_KEY, 'DO-NOT-PRINT');
assert.equal(process.argv[2], '$(touch BAD); argument with spaces');
assert.equal(process.cwd(), ${JSON.stringify(f.source)});
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
    { BPP_R2_SECRET_ACCESS_KEY: '' }
  );
  assert.equal(result.status, 37, result.stdout + result.stderr);
  assert.doesNotMatch(result.stdout + result.stderr, /DO-NOT-PRINT/);
  assert.equal(fs.existsSync(path.join(f.source, 'BAD')), false);
});

test('CLI signing run exposes only a staging path and removes it even when the command fails', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.ini',
    ini({
      signing:
        'APPLE_API_ISSUER=DO-NOT-PRINT\nTAURI_SIGNING_PRIVATE_KEY_PASSWORD=DO-NOT-PRINT\n'
    })
  );
  const script = f.put(
    f.source,
    'child.mjs',
    `import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
const directory = process.env.BPP_SIGNING_SECRETS_DIR;
assert.ok(!process.env.APPLE_API_ISSUER);
assert.ok(!process.env.TAURI_SIGNING_PRIVATE_KEY_PASSWORD);
assert.equal(fs.readFileSync(path.join(directory, 'apple-api-issuer'), 'utf8'), 'DO-NOT-PRINT');
process.exit(9);
`
  );
  const result = cli(f, ['run', 'signing', '--', process.execPath, script], {
    BPP_SIGNING_SECRETS_DIR: '',
    APPLE_API_ISSUER: '',
    TAURI_SIGNING_PRIVATE_KEY_PASSWORD: ''
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
    /Unknown profile; use server, analyzer, release, signing, mod or cloudflare/
  );
});
