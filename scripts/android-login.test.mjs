import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { applyLogin, updatePreferences } from './android-login.mjs';

const email = 'test+android@example.invalid';
const password = 'secret <&> " \' $() ` %s 中文';

function parsed(xml) {
  const result = spawnSync(
    'python3',
    [
      '-c',
      'import sys,json,xml.etree.ElementTree as E; print(json.dumps([(e.tag,e.attrib,e.text) for e in E.fromstring(sys.stdin.read())]))'
    ],
    { input: xml, encoding: 'utf8' }
  );
  assert.equal(result.status, 0);
  return JSON.parse(result.stdout);
}

function fixture(
  t,
  config = `[game]\nBPP_GAME_EMAIL=${email}\nBPP_GAME_PASSWORD=from-config\n`
) {
  const home = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-login-'));
  t.after(() => fs.rmSync(home, { recursive: true, force: true }));
  fs.writeFileSync(path.join(home, 'config.ini'), config);
  return {
    ...process.env,
    BPP_CONFIG_HOME: home,
    BPP_GAME_EMAIL: '',
    BPP_GAME_PASSWORD: ''
  };
}

test('updates only credentials, retaining XML preference types, values and arrays', () => {
  const original = `<map>
    <string name="email">old</string><string name="password">old-secret</string>
    <int name="quality" value="2"/><boolean name="audio" value="true"/>
    <string name="text">a &amp; b</string><set name="set"><string>one</string></set>
  </map>`;
  const actual = parsed(updatePreferences(original, email, password));
  assert.deepEqual(actual.slice(2), parsed(original).slice(2));
  assert.deepEqual(actual.slice(0, 2), [
    ['string', { name: 'email' }, email],
    ['string', { name: 'password' }, password]
  ]);
  assert.match(
    updatePreferences(original, email, password),
    /<string>one<\/string>/
  );
});

test('initializes missing credentials and refuses ambiguous or malformed XML without echoing secrets', () => {
  assert.equal(parsed(updatePreferences('<map />', email, password)).length, 2);
  for (const xml of [
    '<map>secret',
    '<not-map />',
    '<map><int name="password" value="1"/></map>',
    '<map><string name="email"/><string name="email"/></map>',
    '<!DOCTYPE map [<!ENTITY secret "value">]><map />'
  ]) {
    assert.throws(
      () => updatePreferences(xml, email, password),
      (error) => {
        assert.match(error.message, /XML validation\/update/);
        assert.doesNotMatch(error.message, /secret/);
        return true;
      }
    );
  }
});

test('config reaches only explicit debug target via stdin, with readback before restart', (t) => {
  const env = fixture(t);
  env.BPP_GAME_PASSWORD = password;
  const calls = [];
  let saved;
  applyLogin('emulator-5560', {
    env,
    run(command, args, input, childEnv, label) {
      calls.push(label);
      assert.equal(command, 'adb');
      assert.deepEqual(args.slice(0, 2), ['-s', 'emulator-5560']);
      assert.doesNotMatch(args.join(' '), /secret|example.invalid|from-config/);
      assert.equal(childEnv.BPP_GAME_EMAIL, undefined);
      assert.equal(childEnv.BPP_GAME_PASSWORD, undefined);
      assert.match(args.join(' '), /com\.bazaarplusplus\.android\.dev/);
      if (label === 'Reading PlayerPrefs') {
        assert.match(args.at(-1), /\.bak/);
        return '<map><int name="quality" value="2"/></map>';
      }
      if (label === 'Writing PlayerPrefs') saved = input;
      if (label === 'Verifying PlayerPrefs') return saved;
      return '';
    }
  });
  assert.deepEqual(calls, [
    'Debug App access',
    'Stopping debug App',
    'Reading PlayerPrefs',
    'Writing PlayerPrefs',
    'Verifying PlayerPrefs',
    'Opening launcher'
  ]);
  assert.deepEqual(parsed(saved).slice(1), [
    ['string', { name: 'email' }, email],
    ['string', { name: 'password' }, password]
  ]);
  if (process.platform !== 'win32')
    assert.equal(
      fs.statSync(path.join(env.BPP_CONFIG_HOME, 'config.ini')).mode & 0o777,
      0o600
    );
});

test('invalid target or missing credentials never touch adb', (t) => {
  const env = fixture(t, '[game]\nBPP_GAME_EMAIL=\n');
  const run = () => assert.fail('adb must not run');
  for (const serial of ['', '-e', 'emulator-5560;echo secret'])
    assert.throws(
      () => applyLogin(serial, { env, run }),
      /explicit adb device serial/
    );
  assert.throws(
    () => applyLogin('emulator-5560', { env, run }),
    /Set BPP_GAME_EMAIL/
  );
});

test('malformed preferences or failed readback leave the App stopped', (t) => {
  const env = fixture(t);
  for (const malformed of [true, false]) {
    const calls = [];
    assert.throws(
      () =>
        applyLogin('emulator-5560', {
          env,
          run(command, args, input, childEnv, label) {
            calls.push(label);
            if (label === 'Reading PlayerPrefs')
              return malformed ? 'secret' : '<map />';
            return '';
          }
        }),
      malformed ? /XML validation/ : /readback did not match/
    );
    assert.ok(!calls.includes('Opening launcher'));
    if (malformed) assert.ok(!calls.includes('Writing PlayerPrefs'));
  }
});
