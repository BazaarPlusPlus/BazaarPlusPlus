import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const scriptsDir = import.meta.dirname;
const script = path.join(scriptsDir, 'release-secrets.sh');
const workflow = path.join(scriptsDir, '..', 'workflows', 'release.yml');
const table = path.join(scriptsDir, '..', '..', 'release', 'github-secrets.mjs');

// Distinct values so a leak is attributable to one name.
const values = {
  CLOUDFLARE_ACCOUNT_ID: 'value-account-id',
  CLOUDFLARE_API_TOKEN: 'value-operator-token',
  TAURI_SIGNING_PRIVATE_KEY: 'value-updater-key\nline two\n',
  APPLE_SIGNING_IDENTITY: 'Developer ID Application: Example (TEAMID)',
  APPLE_API_ISSUER: 'value-issuer',
  APPLE_API_KEY: 'KEYID1234',
  APPLE_API_KEY_P8: '-----BEGIN PRIVATE KEY-----\nvalue-p8\n',
  APPLE_CERTIFICATE: 'value-certificate',
  APPLE_CERTIFICATE_PASSWORD: 'value-certificate-password'
};

function run(args, secrets = values) {
  const env = { PATH: process.env.PATH, HOME: os.tmpdir() };
  for (const [name, value] of Object.entries(secrets)) env[name] = value;
  return spawnSync('bash', [script, ...args], { env, encoding: 'utf8' });
}

function tableNames(verb) {
  const result = spawnSync(process.execPath, [table, verb], {
    encoding: 'utf8'
  });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim().split('\n').filter(Boolean);
}

function assertNoValueLeak(result) {
  for (const value of Object.values(values)) {
    assert.ok(
      !result.stdout.includes(value) && !result.stderr.includes(value),
      `secret value printed: ${value}`
    );
  }
}

test('release.yml reads exactly the names the table lists, each at its scope', () => {
  const text = fs.readFileSync(workflow, 'utf8');
  const referenced = (kind) =>
    [...new Set([...text.matchAll(new RegExp(`${kind}\\.([A-Z0-9_]+)`, 'g'))].map((m) => m[1]))].sort();
  const names = run(['names']);
  assert.equal(names.status, 0, names.stderr);
  assert.deepEqual(names.stdout.trim().split('\n').sort(), tableNames('names').sort());
  assert.deepEqual(referenced('secrets'), tableNames('secrets').sort());
  assert.deepEqual(referenced('vars'), tableNames('variables').sort());
  assert.deepEqual(Object.keys(values).sort(), tableNames('names').sort());
  assert.doesNotMatch(text, /TAURI_SIGNING_PRIVATE_KEY_PASSWORD|BPP_R2_|BPP_GAME_LIBS_R2_/);
});

test('check passes when every name of the stage is present', () => {
  for (const platform of ['macos', 'windows']) {
    for (const stage of ['prepare', 'release']) {
      const result = run(['check', platform, stage]);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.match(result.stdout, /secrets for the .* stage are present/);
      assertNoValueLeak(result);
    }
  }
});

test('check names each missing name and its local source, then fails', () => {
  const secrets = { ...values, APPLE_API_KEY_P8: '', CLOUDFLARE_ACCOUNT_ID: '' };
  const result = run(['check', 'macos', 'release'], secrets);
  assert.equal(result.status, 1);
  assert.match(
    result.stdout,
    /::error title=Missing secret::APPLE_API_KEY_P8 is not set for this run; its value is: contents of keys\/AuthKey_<APPLE_API_KEY>\.p8/
  );
  assert.match(
    result.stdout,
    /::error title=Missing secret::CLOUDFLARE_ACCOUNT_ID is not set for this run; its value is: config\.ini \[cloudflare\] CLOUDFLARE_ACCOUNT_ID/
  );
  assert.match(result.stderr, /2 required secret\(s\) missing/);
  assertNoValueLeak(result);
});

test('a dry run needs only the operator token and the account id', () => {
  const secrets = {
    CLOUDFLARE_ACCOUNT_ID: values.CLOUDFLARE_ACCOUNT_ID,
    CLOUDFLARE_API_TOKEN: values.CLOUDFLARE_API_TOKEN
  };
  for (const platform of ['macos', 'windows']) {
    assert.equal(run(['check', platform, 'prepare'], secrets).status, 0);
    assert.equal(run(['check', platform, 'release'], secrets).status, 1);
  }
});

test('windows needs no Apple material', () => {
  const secrets = { ...values };
  for (const name of Object.keys(values))
    if (name.startsWith('APPLE_')) secrets[name] = '';
  assert.equal(run(['check', 'windows', 'release'], secrets).status, 0);
  const macos = run(['check', 'macos', 'release'], secrets);
  assert.equal(macos.status, 1);
  assert.match(macos.stderr, /6 required secret\(s\) missing/);
});

test('stage writes the files bundle.sh reads from BPP_SIGNING_SECRETS_DIR; the updater password is optional and outside the table', (t) => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-release-secrets-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const macos = path.join(root, 'macos');
  let result = run(['stage', 'macos', macos], {
    ...values,
    TAURI_SIGNING_PRIVATE_KEY_PASSWORD: 'value-updater-password'
  });
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assertNoValueLeak(result);
  assert.ok(!result.stdout.includes('value-updater-password'));
  assert.deepEqual(fs.readdirSync(macos).sort(), [
    'AuthKey_KEYID1234.p8',
    'apple-api-issuer',
    'apple-api-key',
    'apple-signing-identity',
    'tauri-updater.key',
    'tauri-updater.password'
  ]);
  assert.equal(
    fs.readFileSync(path.join(macos, 'tauri-updater.key'), 'utf8'),
    values.TAURI_SIGNING_PRIVATE_KEY
  );
  assert.equal(
    fs.readFileSync(path.join(macos, 'AuthKey_KEYID1234.p8'), 'utf8'),
    values.APPLE_API_KEY_P8
  );
  assert.equal(
    fs.readFileSync(path.join(macos, 'apple-signing-identity'), 'utf8'),
    values.APPLE_SIGNING_IDENTITY
  );
  if (process.platform !== 'win32') {
    assert.equal(fs.statSync(macos).mode & 0o777, 0o700);
    for (const file of fs.readdirSync(macos))
      assert.equal(fs.statSync(path.join(macos, file)).mode & 0o777, 0o600);
  }

  const windows = path.join(root, 'windows');
  result = run(['stage', 'windows', windows]);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.deepEqual(fs.readdirSync(windows), ['tauri-updater.key']);
});

test('stage refuses to write a partial directory', (t) => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-release-secrets-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const directory = path.join(root, 'macos');
  const result = run(['stage', 'macos', directory], {
    ...values,
    APPLE_API_ISSUER: ''
  });
  assert.equal(result.status, 1);
  assert.match(
    result.stdout,
    /Missing secret::APPLE_API_ISSUER is not set for this run; its value is: config\.ini \[signing\] APPLE_API_ISSUER/
  );
  assert.ok(!fs.existsSync(directory));
});

test('usage errors exit 2', () => {
  assert.equal(run([]).status, 2);
  assert.equal(run(['check', 'linux', 'release']).status, 2);
  assert.equal(run(['check', 'macos', 'ship']).status, 2);
  assert.equal(run(['stage', 'macos']).status, 2);
  assert.equal(run(['optional']).status, 2);
});
