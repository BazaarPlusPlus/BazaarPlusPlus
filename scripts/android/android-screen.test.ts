import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'vite-plus/test';
import type { TestContext } from 'vite-plus/test';
import { inspectScreen } from './android-screen.ts';

const root = path.resolve(import.meta.dirname, '../..');
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

test('android.sh finds the one running device; capture keeps failure evidence and screen-check uses only read-only adb commands', (t) => {
  const cwd = fixture(t);
  for (const file of [
    'scripts/android/android-screen.ts',
    'bazaarplusplus-mod/scripts/android.sh',
    'bazaarplusplus-mod/scripts/lib.sh'
  ]) {
    fs.mkdirSync(path.dirname(path.join(cwd, file)), { recursive: true });
    fs.copyFileSync(path.join(root, file), path.join(cwd, file));
  }
  const adb = path.join(cwd, 'fake-adb');
  fs.writeFileSync(
    adb,
    `#!/bin/bash
set -eu
[[ "\${MSYS_NO_PATHCONV:-}" == 1 || "$*" == devices ]] || exit 93
if [[ "$*" == devices ]]; then
  echo 'List of devices attached'
  cat "$FIXTURE/devices.txt"
  exit 0
fi
[[ "$1" == -s && "$2" == fixture-device ]] || exit 91
shift 2
case "$*" in
  'get-state') echo device ;;
  'logcat -d -v threadtime') cat "$FIXTURE/logcat.txt" ;;
  'exec-out screencap -p') cat "$FIXTURE/screen.png" ;;
  'shell wm size') cat "$FIXTURE/wm-size.txt" ;;
  'shell wm density') cat "$FIXTURE/wm-density.txt" ;;
  'shell settings get system user_rotation') echo 1 ;;
  'shell settings get system accelerometer_rotation') echo 0 ;;
  'shell dumpsys display') echo 'rotation=1' ;;
  'shell run-as com.bazaarplusplus.android.dev true') ;;
  'exec-out run-as com.bazaarplusplus.android.dev cat no_backup/game-external/MelonLoader/MelonLoader/Latest.log') echo 'fixture log' ;;
  'push '*) [[ -f "$2" && "$3" == /data/local/tmp/bpp-staging ]] || exit 94 ;;
  'shell run-as com.bazaarplusplus.android.dev sh -c '*) [[ "$*" == *'cp /data/local/tmp/bpp-staging no_backup/game-external/test/payload.dll'* ]] || exit 95 ;;
  'shell rm -f /data/local/tmp/bpp-staging') ;;
  *) echo "Unexpected/mutating adb command: $*" >&2; exit 92 ;;
esac
`,
    { mode: 0o755 }
  );
  const invoke = (
    command: string,
    name: string,
    serial = '',
    target?: string
  ) =>
    spawnSync(
      'bash',
      [
        'bazaarplusplus-mod/scripts/android.sh',
        command,
        name,
        ...(target ? [target] : [])
      ],
      {
        cwd,
        encoding: 'utf8',
        env: {
          ...process.env,
          ADB: adb.split(path.sep).join('/'),
          ANDROID_SERIAL: serial,
          FIXTURE: cwd.split(path.sep).join('/')
        },
        timeout: 10_000
      }
    );
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
  const hostInput =
    process.platform === 'win32'
      ? spawnSync('bash', ['-c', 'cygpath -u "$1"', 'bash', input], {
          encoding: 'utf8'
        }).stdout.trim()
      : input;
  const put = invoke('put', hostInput, 'fixture-device', 'test/payload.dll');
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
  // verify reads downloaded evidence: it needs no device.
  online('');
  fs.writeFileSync(path.join(out, 'version.txt'), '0.0.0');
  const verify = invoke('verify', out);
  assert.equal(verify.status, 1, verify.stderr);
  assert.match(verify.stderr, /Smoke failed at "Injection"/);
  assert.doesNotMatch(verify.stderr, /Android device/);
});
