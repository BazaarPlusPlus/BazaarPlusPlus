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
  commandEnvironment,
  readConfig,
  sectionValues,
  parseConfig,
  projectionChanges,
  assertExternal,
  stageSigning
} from './local-config.mjs';

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
  const values = (section) => sectionValues(readConfig(home), section);
  return { repository, source, home, put, values };
}

const ini = (sections) =>
  Object.entries(sections)
    .map(([name, body]) => `[${name}]\n${body}`)
    .join('\n');

test('import after setup fills config.ini, keeps data and Apple key meaning, protects permissions, and is repeatable', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, f.repository);
  f.put(
    f.source,
    'bazaarplusplus-analyzer/.env',
    'BPP_DATA_ROOT=../ignored-first-value\nBPP_DATA_ROOT=../durable-state\nBPP_BUNDLE_SYNC_TOKEN=secret-value\n'
  );
  f.put(
    f.source,
    'bazaarplusplus-server/.dev.vars',
    'BUNDLE_SYNC_TOKEN=secret-value\n'
  );
  const signing = 'bazaarplusplus-installer/signing-secrets';
  f.put(f.source, `${signing}/apple-api-key`, 'TEST\n');
  f.put(f.source, `${signing}/apple-signing-identity`, 'Dev ID: A (#1)\n');
  f.put(
    f.source,
    `${signing}/apple-api-key-path`,
    'signing-secrets/AuthKey_TEST.p8\n'
  );
  f.put(f.source, `${signing}/AuthKey_TEST.p8`, 'PRIVATE-TEST-KEY');
  f.put(f.source, `${signing}/tauri-updater.key`, 'UPDATER-KEY');
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
      path.join(f.source, `${signing}/apple-api-key-path`),
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

test('an import conflict changes neither existing credentials nor other files', (t) => {
  const f = fixture(t);
  f.put(f.source, 'bazaarplusplus-analyzer/.env', 'BPP_DATA_ROOT=/old\n');
  f.put(
    f.source,
    'bazaarplusplus-server/.dev.vars',
    'BUNDLE_SYNC_TOKEN=NEW-SECRET\n'
  );
  f.put(
    f.source,
    'bazaarplusplus-installer/signing-secrets/tauri-updater.key',
    'KEY'
  );
  const existing = ini({ server: 'BUNDLE_SYNC_TOKEN=EXISTING-SECRET\n' });
  f.put(f.home, 'config.ini', existing);
  assert.throws(
    () => importCheckout(f.source, f.home, f.repository),
    (error) => {
      assert.match(error.message, /Import conflict: \[server\]/);
      assert.doesNotMatch(error.message, /NEW-SECRET|EXISTING-SECRET/);
      return true;
    }
  );
  assert.equal(
    fs.readFileSync(path.join(f.home, 'config.ini'), 'utf8'),
    existing
  );
  assert.equal(fs.existsSync(path.join(f.home, 'keys')), false);
});

test('an unknown signing-secrets entry is refused before any write', (t) => {
  const f = fixture(t);
  f.put(
    f.source,
    'bazaarplusplus-installer/signing-secrets/extra-token',
    'SECRET'
  );
  assert.throws(
    () => importCheckout(f.source, f.home, f.repository),
    /Unknown signing-secrets entry: extra-token/
  );
  assert.equal(fs.existsSync(f.home), false);
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
    () => commandEnvironment('server', f.home, {}, f.repository)
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

test('config.ini rejects ambiguous text without echoing values', () => {
  for (const [text, pattern] of [
    [
      '[server] # note\nA=SECRET\n',
      /Unknown section header on config.ini line 1/
    ],
    ['[Server]\nA=SECRET\n', /Unknown section header/],
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
      /Multi-line quoted values are unsupported on config.ini line 2/
    ]
  ]) {
    assert.throws(
      () => parseConfig(text),
      (error) => {
        assert.match(error.message, pattern);
        assert.doesNotMatch(error.message, /SECRET/);
        return true;
      }
    );
  }
  const config = parseConfig(
    '# head\r\n\r\n[server]\r\nexport A="x # y"\r\n# kept\r\n\r\n[analyzer]\r\n'
  );
  assert.equal(config.preamble, '# head\n');
  assert.equal(config.sections.get('server'), 'export A="x # y"\n# kept\n');
  assert.deepEqual(sectionValues(config, 'server'), { A: 'x # y' });
  assert.equal(config.sections.get('analyzer'), '');
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
  commandEnvironment('analyzer', f.home, {}, f.repository);
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
    () => commandEnvironment('server', f.home, {}, f.repository),
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
  const env = commandEnvironment(
    'release',
    f.home,
    { BPP_R2_ACCESS_KEY_ID: 'explicit-key' },
    f.repository
  );
  assert.deepEqual(env, {
    BPP_R2_ACCOUNT_ID: 'account',
    BPP_R2_ACCESS_KEY_ID: 'explicit-key',
    BPP_R2_SECRET_ACCESS_KEY: '$(touch never); `exit 1`'
  });
  assert.deepEqual(commandEnvironment('mod', f.home, {}, f.repository), {});
  assert.throws(
    () => commandEnvironment('unknown', f.home, {}, f.repository),
    /Unknown profile/
  );
});

test('missing canonical section cannot silently run with a stale checkout file', (t) => {
  const f = fixture(t);
  f.put(f.repository, 'bazaarplusplus-analyzer/.env', 'BPP_DATA_ROOT=/old\n');
  assert.throws(
    () => commandEnvironment('analyzer', f.home, {}, f.repository),
    /Missing \[analyzer\] in config.ini/
  );
});

test('signing staging holds bundle.sh files only; an explicit directory is used unmixed', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'config.ini',
    ini({
      signing:
        'APPLE_API_KEY=K1\nAPPLE_API_ISSUER=issuer\nAPPLE_API_KEY_PATH=keys/Other.p8\n'
    })
  );
  f.put(f.home, 'keys/tauri-updater.key', 'UPDATER');
  f.put(f.home, 'keys/notes.txt', 'NOT-KEY-MATERIAL');
  const staged = stageSigning(f.home, { PATH: 'p' });
  const directory = staged.env.BPP_SIGNING_SECRETS_DIR;
  assert.deepEqual(Object.keys(staged.env).sort(), [
    'BPP_SIGNING_SECRETS_DIR',
    'PATH'
  ]);
  assert.deepEqual(fs.readdirSync(directory).sort(), [
    'apple-api-issuer',
    'apple-api-key',
    'apple-api-key-path',
    'tauri-updater.key'
  ]);
  assert.equal(
    fs.readFileSync(path.join(directory, 'apple-api-key-path'), 'utf8'),
    path.join(f.home, 'keys/Other.p8')
  );
  if (process.platform !== 'win32')
    assert.equal(fs.statSync(directory).mode & 0o777, 0o700);
  staged.cleanup();
  assert.equal(fs.existsSync(directory), false);
  const explicit = stageSigning(f.home, { BPP_SIGNING_SECRETS_DIR: '/given' });
  assert.deepEqual(explicit.env, { BPP_SIGNING_SECRETS_DIR: '/given' });
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
    ini({ signing: 'APPLE_API_ISSUER=DO-NOT-PRINT\n' })
  );
  const script = f.put(
    f.source,
    'child.mjs',
    `import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
const directory = process.env.BPP_SIGNING_SECRETS_DIR;
assert.ok(!process.env.APPLE_API_ISSUER);
assert.equal(fs.readFileSync(path.join(directory, 'apple-api-issuer'), 'utf8'), 'DO-NOT-PRINT');
process.exit(9);
`
  );
  const result = cli(f, ['run', 'signing', '--', process.execPath, script], {
    BPP_SIGNING_SECRETS_DIR: '',
    APPLE_API_ISSUER: ''
  });
  assert.equal(result.status, 9, result.stdout + result.stderr);
  assert.doesNotMatch(result.stdout + result.stderr, /DO-NOT-PRINT/);
  assert.deepEqual(
    fs.readdirSync(f.home).filter((entry) => entry.startsWith('.signing-')),
    []
  );
});
