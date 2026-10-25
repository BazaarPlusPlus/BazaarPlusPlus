import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { inspectScreen } from './android-screen.mjs';

const root = path.resolve(import.meta.dirname, '..');
function png(width, height) {
  const buffer = Buffer.alloc(24);
  Buffer.from('89504e470d0a1a0a', 'hex').copy(buffer);
  buffer.write('IHDR', 12);
  buffer.writeUInt32BE(width, 16);
  buffer.writeUInt32BE(height, 20);
  return buffer;
}
function fixture(t) {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-screen-'));
  t.after(() => fs.rmSync(cwd, { recursive: true, force: true }));
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
  assert.deepEqual(report.unity.safeArea, [136, 0, 2264, 1080]);
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
]) {
  test(`rejects unsuitable ${file}: ${pattern.source}`, (t) => {
    const cwd = fixture(t);
    fs.writeFileSync(path.join(cwd, file), content);
    assert.match(inspectScreen(cwd, 'phone').problems.join('\n'), pattern);
  });
}

test('capture keeps failure evidence and screen-check uses only read-only adb commands', (t) => {
  const cwd = fixture(t);
  for (const file of [
    'scripts/android-screen.mjs',
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
  'pull /sdcard/Android/data/com.tempogames.thebazaar/files/MelonLoader/MelonLoader/Latest.log '*) echo 'fixture log' > "$3" ;;
  *) echo "Unexpected/mutating adb command: $*" >&2; exit 92 ;;
esac
`,
    { mode: 0o755 }
  );
  const invoke = (command, name) =>
    spawnSync(
      'bash',
      ['bazaarplusplus-mod/scripts/android.sh', command, name],
      {
        cwd,
        encoding: 'utf8',
        env: {
          ...process.env,
          ADB: adb.split(path.sep).join('/'),
          BPP_ANDROID_SERIAL: 'fixture-device',
          FIXTURE: cwd.split(path.sep).join('/')
        },
        timeout: 10_000
      }
    );
  const good = invoke('screen-check', 'good');
  assert.equal(good.status, 0, good.stderr);
  fs.writeFileSync(path.join(cwd, 'screen.png'), png(1080, 2400));
  const bad = invoke('screen-check', 'bad');
  assert.equal(bad.status, 1, bad.stderr);
  assert.match(bad.stderr, /not landscape/);
  const out = path.join(cwd, 'bazaarplusplus-mod/artifacts/android/bad');
  assert.ok(fs.existsSync(path.join(out, 'screen.png')));
  assert.ok(fs.existsSync(path.join(out, 'Latest.log')));
  assert.equal(
    JSON.parse(fs.readFileSync(path.join(out, 'environment.json'))).screenshot
      .height,
    2400
  );
  const capture = invoke('capture', 'diagnostic');
  assert.equal(capture.status, 0, capture.stderr);
});
