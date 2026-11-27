import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'vite-plus/test';
import type { TestContext } from 'vite-plus/test';
import { inspectScreen } from './android-screen.ts';
import { smokeWaitState, updateIniSetting } from './mod-loop.ts';

const root = path.resolve(import.meta.dirname, '../..');
const modLoopScript = path.join(root, 'scripts/android/mod-loop.ts');

function png(width: number, height: number): Buffer {
  const buffer = Buffer.alloc(24);
  Buffer.from('89504e470d0a1a0a', 'hex').copy(buffer);
  buffer.write('IHDR', 12);
  buffer.writeUInt32BE(width, 16);
  buffer.writeUInt32BE(height, 20);
  return buffer;
}
function fixture(t: TestContext): string {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-screen-'));
  t.onTestFinished(() => fs.rmSync(cwd, { recursive: true, force: true }));
  fs.writeFileSync(path.join(cwd, 'screen.png'), png(2400, 1080));
  const files = {
    'wm-size.txt': 'Physical size: 1080x2400',
    'wm-density.txt': 'Physical density: 420',
    'user-rotation.txt': '1',
    'accelerometer-rotation.txt': '0',
    'logcat.txt': 'screen=2400x1080@420|136,0,2264,1080'
  };
  for (const [file, text] of Object.entries(files))
    fs.writeFileSync(path.join(cwd, file), text);
  return cwd;
}

test('landscape is taken from screenshot, not the portrait physical panel; records safe area', (t) => {
  const report = inspectScreen(fixture(t), 'phone');
  assert.deepEqual(report.problems, []);
  assert.deepEqual(report.unity!.safeArea, [136, 0, 2264, 1080]);
  assert.equal(report.userRotation, '1');
  assert.equal(report.serial, 'phone');
});

for (const [file, content, pattern] of [
  ['screen.png', png(1080, 2400), /not landscape/],
  [
    'wm-size.txt',
    'Physical size: 1080x2400\nOverride size: 2560x1600',
    /overridden/
  ],
  [
    'wm-density.txt',
    'Physical density: 420\nOverride density: 320',
    /overridden/
  ],
  ['logcat.txt', 'screen=1080x2400@420|0,0,1080,2400', /differs/]
] satisfies [string, string | Buffer, RegExp][]) {
  test(`rejects unsuitable ${file}: ${pattern.source}`, (t) => {
    const cwd = fixture(t);
    fs.writeFileSync(path.join(cwd, file), content);
    assert.match(inspectScreen(cwd, 'phone').problems.join('\n'), pattern);
  });
}

test('mod-loop finds the one running device; capture keeps failure evidence and screen-check uses only read-only adb commands', (t) => {
  const cwd = fixture(t);
  const adb = path.join(cwd, 'fake-adb');
  const adbScript = path.join(cwd, 'fake-adb.mjs');
  fs.writeFileSync(
    adbScript,
    `import fs from 'node:fs';
import path from 'node:path';
const args = process.argv.slice(2);
const fix = process.env.FIXTURE;
if (process.env.MSYS_NO_PATHCONV !== '1') process.exit(93);
if (args.join(' ') === 'devices') {
  process.stdout.write('List of devices attached\\n' + fs.readFileSync(path.join(fix, 'devices.txt'), 'utf8'));
  process.exit(0);
}
if (args[0] !== '-s' || args[1] !== 'fixture-device') process.exit(91);
const cmd = args.slice(2).join(' ');
if (cmd === 'get-state') process.stdout.write('device\\n');
else if (cmd === 'logcat -d -v threadtime') process.stdout.write(fs.readFileSync(path.join(fix, 'logcat.txt')));
else if (cmd === 'exec-out screencap -p') process.stdout.write(fs.readFileSync(path.join(fix, 'screen.png')));
else if (cmd === 'shell wm size') process.stdout.write(fs.readFileSync(path.join(fix, 'wm-size.txt')));
else if (cmd === 'shell wm density') process.stdout.write(fs.readFileSync(path.join(fix, 'wm-density.txt')));
else if (cmd === 'shell settings get system user_rotation') process.stdout.write('1\\n');
else if (cmd === 'shell settings get system accelerometer_rotation') process.stdout.write('0\\n');
else if (cmd === 'shell dumpsys display') process.stdout.write('rotation=1\\n');
else if (cmd === 'shell run-as com.bazaarplusplus.android.dev true') {}
else if (cmd === 'exec-out run-as com.bazaarplusplus.android.dev cat no_backup/game-external/MelonLoader/MelonLoader/Latest.log') process.stdout.write('fixture log\\n');
else if (args[2] === 'push') {
  if (!fs.existsSync(args[3]) || args[4] !== '/data/local/tmp/bpp-staging') process.exit(94);
} else if (cmd.startsWith('shell run-as com.bazaarplusplus.android.dev sh -c ')) {
  if (!cmd.includes('cp /data/local/tmp/bpp-staging no_backup/game-external/test/payload.dll')) process.exit(95);
} else if (cmd === 'shell rm -f /data/local/tmp/bpp-staging') {}
else {
  process.stderr.write('Unexpected/mutating adb command: ' + cmd + '\\n');
  process.exit(92);
}
`
  );
  fs.writeFileSync(
    adb,
    `#!/usr/bin/env bash\nexec '${process.execPath}' '${adbScript}' "$@"\n`,
    { mode: 0o755 }
  );
  const modDir = path.join(cwd, 'bazaarplusplus-mod');
  fs.mkdirSync(modDir, { recursive: true });

  const invoke = (
    command: string,
    name: string,
    serial = '',
    target?: string
  ) => {
    const runner = path.join(cwd, 'run-loop.mjs');
    fs.writeFileSync(
      runner,
      `import { cliMain } from ${JSON.stringify(modLoopScript)};
process.exitCode = await cliMain(${JSON.stringify([command, name, ...(target ? [target] : [])])}, {
  workspaceRoot: ${JSON.stringify(cwd)},
  modRoot: ${JSON.stringify(modDir)},
  env: {
    ...process.env,
    ADB: ${JSON.stringify(adb)},
    ANDROID_SERIAL: ${JSON.stringify(serial)},
    FIXTURE: ${JSON.stringify(cwd)}
  }
});
`
    );
    return spawnSync(process.execPath, [runner], {
      cwd,
      encoding: 'utf8',
      timeout: 15_000
    });
  };

  const online = (text: string) =>
    fs.writeFileSync(path.join(cwd, 'devices.txt'), text);
  for (const [devices, pattern] of [
    ['', /No Android device is online/],
    [
      'fixture-device\tdevice\nemulator-5558\tdevice\n',
      /More than one Android device is running \(fixture-device device, emulator-5558 device\)/
    ],
    ['fixture-device\toffline\n', /'fixture-device' is offline/]
  ] satisfies [string, RegExp][]) {
    online(devices);
    const refused = invoke('capture', 'refused');
    assert.equal(refused.status, 1, refused.stderr);
    assert.match(refused.stderr, pattern);
  }
  online('phone\tdevice\r\r\nfixture-device\tdevice\r\r\n');
  const selected = invoke('screen-check', 'selected', 'fixture-device');
  assert.equal(selected.status, 0, selected.stderr);
  const missing = invoke('capture', 'missing', 'absent');
  assert.equal(missing.status, 1, missing.stderr);
  assert.match(
    missing.stderr,
    /Selected Android device 'absent' is not connected/
  );
  online('phone\tdevice\nfixture-device\toffline\n');
  const offline = invoke('capture', 'offline', 'fixture-device');
  assert.equal(offline.status, 1, offline.stderr);
  assert.match(offline.stderr, /'fixture-device' is offline/);
  online('fixture-device\tdevice\n');
  const good = invoke('screen-check', 'good');
  assert.equal(good.status, 0, good.stderr);
  const input = path.join(cwd, 'input payload.dll');
  fs.writeFileSync(input, 'payload');
  const put = invoke('put', input, 'fixture-device', 'test/payload.dll');
  assert.equal(put.status, 0, put.stderr);
  fs.writeFileSync(path.join(cwd, 'screen.png'), png(1080, 2400));
  const bad = invoke('screen-check', 'bad');
  assert.equal(bad.status, 1, bad.stderr);
  assert.match(bad.stderr, /not landscape/);
  const out = path.join(cwd, 'bazaarplusplus-mod/artifacts/android/bad');
  assert.ok(fs.existsSync(path.join(out, 'screen.png')));
  assert.ok(fs.existsSync(path.join(out, 'Latest.log')));
  assert.equal(
    JSON.parse(fs.readFileSync(path.join(out, 'environment.json'), 'utf8'))
      .screenshot.height,
    2400
  );
  const capture = invoke('capture', 'diagnostic');
  assert.equal(capture.status, 0, capture.stderr);
  online('');
  fs.writeFileSync(path.join(out, 'version.txt'), '0.0.0');
  const verify = invoke('verify', out);
  assert.equal(verify.status, 1, verify.stderr);
  assert.match(verify.stderr, /Smoke failed at "Injection"/);
  assert.doesNotMatch(verify.stderr, /Android device/);
});

test('smoke waits for a hooks summary that counts the version label, not the first one after the main menu', () => {
  const state = (need: boolean, lines: string[]) =>
    smokeWaitState(need, `${lines.join('\n')}\n`);
  const event = (name: string, fields = '') =>
    `[12:00:00.000] [BazaarPlusPlus] [BPP][Android] event=android.${name} ${fields}`;
  const menu = event('scene.loaded', 'name=MainMenuScene mode=Single');
  const summary = (hits: number) =>
    event('hooks.summary', `version_label_hits=${hits} replay_starts=0`);
  const label = event('version_label.hit', 'hits=1 write=true');

  // Game 1.0.13113: a summary follows the main menu before the label is written.
  const early = [event('plugin.loaded'), menu, summary(0)];
  assert.equal(state(true, early), 'menu');
  assert.equal(state(false, early), 'ready');
  assert.equal(state(true, [...early, label]), 'menu');
  assert.equal(state(true, [...early, label, summary(1)]), 'ready');
  assert.equal(state(true, [menu, summary(12)]), 'ready');

  assert.equal(state(true, [summary(1), label]), 'waiting');
  assert.equal(state(true, [summary(1), menu]), 'waiting');
  assert.equal(state(false, [summary(1), menu]), 'waiting');
  assert.equal(state(true, []), 'waiting');
});

test('updateIniSetting updates existing keys or appends new sections cleanly', () => {
  assert.equal(
    updateIniSetting(
      '[Android]\nPatchMode = Disabled\n',
      'Android',
      'PatchMode',
      'Enabled'
    ),
    '[Android]\nPatchMode = Enabled\n'
  );
  assert.equal(
    updateIniSetting('[Other]\nKey = 1\n', 'Android', 'PatchMode', 'Observe'),
    '[Other]\nKey = 1\n\n[Android]\n\nPatchMode = Observe\n'
  );
});
