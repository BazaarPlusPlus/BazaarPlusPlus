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
  readEnvironment,
  projectionChanges,
  assertExternal
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
  return { repository, source, home, put };
}

test('import preserves relative data and Apple key meaning, protects permissions, and is repeatable', (t) => {
  const f = fixture(t);
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
  f.put(
    f.source,
    'bazaarplusplus-installer/signing-secrets/apple-api-key-path',
    'signing-secrets/AuthKey_TEST.p8\n'
  );
  f.put(
    f.source,
    'bazaarplusplus-installer/signing-secrets/AuthKey_TEST.p8',
    'PRIVATE-TEST-KEY'
  );
  const imported = importCheckout(f.source, f.home, f.repository);
  assert.equal(imported.length, 4);
  assert.deepEqual(importCheckout(f.source, f.home, f.repository), imported);
  assert.equal(
    readEnvironment(path.join(f.home, 'analyzer.env')).BPP_DATA_ROOT,
    path.join(f.source, 'durable-state').replaceAll('\\', '/')
  );
  assert.equal(fs.existsSync(path.join(f.source, 'durable-state')), false);
  assert.equal(
    fs
      .readFileSync(
        path.join(f.home, 'signing-secrets/apple-api-key-path'),
        'utf8'
      )
      .trim(),
    path.join(f.home, 'signing-secrets/AuthKey_TEST.p8')
  );
  assert.equal(
    fs.readFileSync(
      path.join(
        f.source,
        'bazaarplusplus-installer/signing-secrets/apple-api-key-path'
      ),
      'utf8'
    ),
    'signing-secrets/AuthKey_TEST.p8\n'
  );
  if (process.platform !== 'win32') {
    assert.equal(fs.statSync(f.home).mode & 0o777, 0o700);
    for (const file of imported)
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
  f.put(f.home, 'server.dev.vars', 'BUNDLE_SYNC_TOKEN=EXISTING-SECRET\n');
  assert.throws(
    () => importCheckout(f.source, f.home, f.repository),
    (error) => {
      assert.match(error.message, /Import conflict: server.dev.vars/);
      assert.doesNotMatch(error.message, /NEW-SECRET|EXISTING-SECRET/);
      return true;
    }
  );
  assert.equal(fs.existsSync(path.join(f.home, 'analyzer.env')), false);
  assert.equal(
    fs.readFileSync(path.join(f.home, 'server.dev.vars'), 'utf8'),
    'BUNDLE_SYNC_TOKEN=EXISTING-SECRET\n'
  );
});

test('setup preserves existing credentials while tightening their POSIX permissions', (t) => {
  const f = fixture(t);
  const file = f.put(
    f.home,
    'release-r2.env',
    'BPP_R2_SECRET_ACCESS_KEY=KEEP\n'
  );
  fs.chmodSync(file, 0o644);
  initializeConfig(f.home, f.repository);
  assert.equal(
    fs.readFileSync(file, 'utf8'),
    'BPP_R2_SECRET_ACCESS_KEY=KEEP\n'
  );
  if (process.platform !== 'win32')
    assert.equal(fs.statSync(file).mode & 0o777, 0o600);
});

test('central edits refresh before use; local edits cause conflict before any projection is replaced', (t) => {
  const f = fixture(t);
  initializeConfig(f.home, f.repository);
  refreshProjections(f.home, f.repository);
  f.put(f.home, 'analyzer.env', 'BPP_DATA_ROOT=/changed\n');
  commandEnvironment('analyzer', f.home, {}, f.repository);
  assert.equal(
    readEnvironment(path.join(f.repository, 'bazaarplusplus-analyzer/.env'))
      .BPP_DATA_ROOT,
    '/changed'
  );
  f.put(f.home, 'analyzer.env', 'BPP_DATA_ROOT=/next\n');
  f.put(
    f.repository,
    'bazaarplusplus-server/.dev.vars',
    'BUNDLE_SYNC_TOKEN=LOCAL-EDIT\n'
  );
  assert.throws(
    () => commandEnvironment('server', f.home, {}, f.repository),
    /Local configuration conflict/
  );
  assert.equal(
    readEnvironment(path.join(f.repository, 'bazaarplusplus-analyzer/.env'))
      .BPP_DATA_ROOT,
    '/changed'
  );
  assert.equal(
    readEnvironment(path.join(f.repository, 'bazaarplusplus-server/.dev.vars'))
      .BUNDLE_SYNC_TOKEN,
    'LOCAL-EDIT'
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

test('scoped environment only injects allowed keys; explicit environment wins and shell text stays literal', (t) => {
  const f = fixture(t);
  f.put(
    f.home,
    'release-r2.env',
    'BPP_R2_ACCOUNT_ID=account\nBPP_R2_ACCESS_KEY_ID=file-key\nBPP_R2_SECRET_ACCESS_KEY="$(touch never); `exit 1`"\nNODE_OPTIONS=--bad\nCLOUDFLARE_API_TOKEN=unrelated\n'
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

test('missing canonical config cannot silently run with a stale checkout file', (t) => {
  const f = fixture(t);
  f.put(f.repository, 'bazaarplusplus-analyzer/.env', 'BPP_DATA_ROOT=/old\n');
  assert.throws(
    () => commandEnvironment('analyzer', f.home, {}, f.repository),
    /Missing analyzer.env/
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

test('CLI preserves cwd, literal arguments, child status and keeps config values out of output', (t) => {
  const f = fixture(t);
  f.put(f.home, 'release-r2.env', 'BPP_R2_SECRET_ACCESS_KEY=DO-NOT-PRINT\n');
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
  const result = spawnSync(
    process.execPath,
    [
      path.join(root, 'scripts/workspace.mjs'),
      'run',
      'release',
      '--',
      process.execPath,
      script,
      '$(touch BAD); argument with spaces'
    ],
    {
      cwd: f.source,
      env: {
        ...process.env,
        BPP_CONFIG_HOME: f.home,
        BPP_R2_SECRET_ACCESS_KEY: ''
      },
      encoding: 'utf8'
    }
  );
  assert.equal(result.status, 37, result.stdout + result.stderr);
  assert.doesNotMatch(result.stdout + result.stderr, /DO-NOT-PRINT/);
  assert.equal(fs.existsSync(path.join(f.source, 'BAD')), false);
});
