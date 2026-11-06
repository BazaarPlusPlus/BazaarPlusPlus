import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import {
  cloneAvd,
  configuredProfile,
  localAvds,
  menuReady,
  main,
  parseIni,
  profiles,
  reconcileAvd,
  removeAvd
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

test('an existing AVD gets the matrix screen in place and keeps its disks', (t) => {
  const p = seed(t);
  const profile = profiles[1];
  cloneAvd(p, 'seed', profile);
  const copy = path.join(p.avds, `${profile.name}.avd`);
  const file = path.join(copy, 'config.ini');
  const config = parseIni(fs.readFileSync(file, 'utf8'));
  fs.writeFileSync(
    file,
    Object.entries({
      ...config,
      'hw.lcd.density': '640',
      'skin.name': '1080x2400',
      'hw.keyboard': 'yes'
    })
      .map(([key, value]) => `${key}=${value}\n`)
      .join('')
  );
  const disk = fs.readFileSync(path.join(copy, 'userdata-qemu.img'), 'utf8');
  assert.equal(reconcileAvd(p, profile), true);
  const rewritten = parseIni(fs.readFileSync(file, 'utf8'));
  assert.equal(configuredProfile(rewritten, profile), true);
  assert.equal(rewritten['skin.name'], undefined);
  assert.equal(rewritten['hw.keyboard'], 'yes');
  assert.equal(
    fs.readFileSync(path.join(copy, 'userdata-qemu.img'), 'utf8'),
    disk
  );
  assert.equal(reconcileAvd(p, profile), false);
});

test('an AVD on another system image needs --recreate and stays untouched', (t) => {
  const p = seed(t);
  const profile = profiles[1];
  cloneAvd(p, 'seed', profile);
  const file = path.join(p.avds, `${profile.name}.avd`, 'config.ini');
  const other = fs
    .readFileSync(file, 'utf8')
    .replace('android-35', 'android-34');
  fs.writeFileSync(file, other);
  assert.throws(() => reconcileAvd(p, profile), /--recreate/);
  assert.equal(fs.readFileSync(file, 'utf8'), other);
});

test('removing an AVD deletes only the layout cloning writes', (t) => {
  const p = seed(t);
  const profile = profiles[1];
  cloneAvd(p, 'seed', profile);
  removeAvd(p, profile.name);
  assert.equal(fs.existsSync(path.join(p.avds, `${profile.name}.avd`)), false);
  assert.equal(fs.existsSync(path.join(p.avds, `${profile.name}.ini`)), false);
  cloneAvd(p, 'seed', profile);
  fs.writeFileSync(
    path.join(p.avds, `${profile.name}.ini`),
    `path=${p.directory}\n`
  );
  assert.throws(() => removeAvd(p, profile.name), /delete that AVD by hand/);
  assert.equal(fs.existsSync(p.directory), true);
});

test('a missing seed names the AVDs that can stand in for it', (t) => {
  const p = seed(t);
  const other = path.join(p.avds, 'old.avd');
  fs.mkdirSync(other);
  fs.writeFileSync(path.join(p.avds, 'old.ini'), `path=${other}\n`);
  fs.writeFileSync(
    path.join(other, 'config.ini'),
    'image.sysdir.1=system-images/android-34/default/arm64-v8a/\nabi.type=arm64-v8a\n'
  );
  assert.deepEqual(localAvds(p), [
    { name: 'old', seedable: false },
    { name: 'seed', seedable: true }
  ]);
  assert.throws(
    () => cloneAvd(p, profiles[0].name, profiles[1]),
    /can be the seed: seed\. Rerun init with --seed seed\./
  );
  fs.rmSync(path.join(p.avds, 'seed.ini'));
  assert.throws(
    () => cloneAvd(p, profiles[0].name, profiles[1]),
    /no AVD here uses/
  );
});

test('--recreate takes one NAME that is not the seed', async () => {
  await assert.rejects(main(['init', '--recreate']), /single NAME/);
  await assert.rejects(
    main(['init', profiles[0].name, '--recreate']),
    /is the seed/
  );
});

test('unsupported login timeouts fail before provisioning a device', async () => {
  for (const timeout of ['0', '601', 'NaN'])
    await assert.rejects(
      main(['init', '--timeout', timeout]),
      /between 1 and 600/
    );
});
