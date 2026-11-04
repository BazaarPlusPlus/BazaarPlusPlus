import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import {
  cloneAvd,
  configuredProfile,
  menuReady,
  parseIni,
  profiles
} from './setup-device.mjs';

test('login readiness requires a menu and a later hooks summary from the fresh log', () => {
  const menu =
    '[BPP][Android] event=android.scene.loaded index=-1 name=MainMenuScene\n';
  const summary = '[BPP][Android] event=android.hooks.summary mode=enabled\n';
  assert.equal(menuReady(''), false);
  assert.equal(menuReady(summary), false);
  assert.equal(menuReady(menu), false);
  assert.equal(menuReady(summary + menu), false);
  assert.equal(menuReady(menu + summary), true);
});

function seed(t, backing = 'userdata-qemu.img') {
  const avds = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp avds '));
  t.after(() => fs.rmSync(avds, { recursive: true, force: true }));
  const directory = path.join(avds, 'seed.avd');
  fs.mkdirSync(directory);
  fs.writeFileSync(path.join(avds, 'seed.ini'), `path=${directory}\n`);
  fs.writeFileSync(
    path.join(directory, 'config.ini'),
    [
      'image.sysdir.1=system-images/android-35/google_apis_playstore/arm64-v8a/',
      'abi.type=arm64-v8a',
      'hw.lcd.width=1080',
      'hw.lcd.height=2400',
      'hw.lcd.density=420',
      'hw.initialOrientation=landscape',
      'skin.path=/old/skin',
      'sdcard.path=/old/sdcard.img',
      'disk.dataPartition.path=/old/userdata.img'
    ].join('\n')
  );
  const files = [
    'userdata-qemu.img',
    'userdata-qemu.img.qcow2',
    'encryptionkey.img',
    'encryptionkey.img.qcow2',
    'sdcard.img'
  ];
  for (const file of files)
    fs.writeFileSync(path.join(directory, file), `private ${file}`);
  for (const file of [
    'hardware-qemu.ini',
    'hardware-qemu.ini.lock',
    'cache.img'
  ])
    fs.writeFileSync(path.join(directory, file), 'stale');
  fs.mkdirSync(path.join(directory, 'snapshots'));
  fs.writeFileSync(path.join(directory, 'snapshots/stale'), 'stale');
  const qemuImg = path.join(avds, 'qemu-img');
  fs.writeFileSync(
    qemuImg,
    `#!/usr/bin/env node\nconst fs = require('node:fs');\nif (process.argv[2] === 'convert') fs.copyFileSync(process.argv.at(-2), process.argv.at(-1));\nelse console.log(JSON.stringify(process.argv.at(-1).endsWith('userdata-qemu.img.qcow2') ? {'backing-filename': ${JSON.stringify(backing)}} : {}));\n`,
    { mode: 0o755 }
  );
  return { avds, qemuImg, directory, files };
}

test('cloning preserves independent account disks and sets native geometry without stale snapshots', (t) => {
  const p = seed(t);
  const originalConfig = fs.readFileSync(
    path.join(p.directory, 'config.ini'),
    'utf8'
  );
  const profile = profiles[1];
  cloneAvd(p, 'seed', profile);
  const copy = path.join(p.avds, `${profile.name}.avd`);
  assert.deepEqual(
    fs.readdirSync(copy).sort(),
    [...p.files.filter((file) => file.endsWith('.img')), 'config.ini'].sort()
  );
  for (const file of p.files.filter((name) => name.endsWith('.img')))
    assert.equal(
      fs.readFileSync(path.join(copy, file), 'utf8'),
      `private ${p.files.includes(`${file}.qcow2`) ? `${file}.qcow2` : file}`
    );
  const config = parseIni(
    fs.readFileSync(path.join(copy, 'config.ini'), 'utf8')
  );
  assert.equal(configuredProfile(config, profile), true);
  assert.equal(config['sdcard.path'], undefined);
  assert.equal(config['skin.path'], undefined);
  fs.writeFileSync(path.join(copy, 'userdata-qemu.img'), 'new session');
  assert.equal(
    fs.readFileSync(path.join(p.directory, 'userdata-qemu.img.qcow2'), 'utf8'),
    'private userdata-qemu.img.qcow2'
  );
  assert.equal(
    fs.readFileSync(path.join(p.directory, 'config.ini'), 'utf8'),
    originalConfig
  );
  assert.throws(() => cloneAvd(p, 'seed', profile), /will not be overwritten/);
});

test('cloning rejects a disk that would still depend on the seed path', (t) => {
  const p = seed(t, '/seed/userdata-qemu.img');
  assert.throws(
    () => cloneAvd(p, 'seed', profiles[1]),
    /external backing file/
  );
  assert.equal(
    fs.existsSync(path.join(p.avds, `${profiles[1].name}.avd`)),
    false
  );
});

test('matrix check rejects a renamed AVD with the wrong actual geometry or image', (t) => {
  const p = seed(t);
  const config = parseIni(
    fs.readFileSync(path.join(p.directory, 'config.ini'), 'utf8')
  );
  assert.equal(configuredProfile(config, profiles[0]), true);
  for (const [key, value] of [
    ['hw.lcd.density', '640'],
    ['hw.initialOrientation', 'portrait'],
    ['abi.type', 'x86_64']
  ])
    assert.equal(
      configuredProfile({ ...config, [key]: value }, profiles[0]),
      false
    );
});
