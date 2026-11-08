import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import * as childProcess from 'node:child_process';
import { test, vi } from 'vitest';
import type { TestContext } from 'vitest';
import {
  cloneAvd,
  configuredProfile,
  listedDevices,
  localAvds,
  menuReady,
  main,
  onlyDevice,
  parseIni,
  profiles,
  reconcileAvd,
  removeAvd,
  hostImage,
  assertArm64Runtime,
  assertEmulatorsStopped
} from './setup-device.ts';
import { androidSdkPaths } from './android-sdk.ts';

vi.mock('node:child_process', async (importOriginal) => {
  const actual = await importOriginal<typeof childProcess>();
  return { ...actual, spawnSync: vi.fn(actual.spawnSync) };
});

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

function seed(
  t: TestContext,
  backing = 'userdata-qemu.img',
  image = hostImage('darwin', 'arm64')
) {
  const avds = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp avds '));
  t.onTestFinished(() => fs.rmSync(avds, { recursive: true, force: true }));
  const directory = path.join(avds, 'seed.avd');
  fs.mkdirSync(directory);
  fs.writeFileSync(path.join(avds, 'seed.ini'), `path=${directory}\n`);
  fs.writeFileSync(
    path.join(directory, 'config.ini'),
    [
      `image.sysdir.1=${image.path.replaceAll('/', '\\')}`,
      `abi.type=${image.abi}`,
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
  const mock = vi
    .mocked(childProcess.spawnSync)
    .mockImplementation((binary, args) => {
      assert.equal(binary, qemuImg);
      assert.ok(Array.isArray(args));
      let stdout = '';
      if (args[0] === 'convert') {
        assert.deepEqual(args.slice(0, 4), ['convert', '-n', '-O', 'raw']);
        assert.equal(fs.statSync(args.at(-1)!).size, 4096);
        fs.copyFileSync(args.at(-2)!, args.at(-1)!);
      } else {
        assert.deepEqual(args.slice(0, 2), ['info', '--output=json']);
        stdout = JSON.stringify(
          args.at(-1)!.endsWith('userdata-qemu.img.qcow2')
            ? { 'backing-filename': backing, 'virtual-size': 4096 }
            : { 'virtual-size': 4096 }
        );
      }
      return {
        pid: 1,
        output: [null, stdout, ''],
        stdout,
        stderr: '',
        status: 0,
        signal: null
      };
    });
  t.onTestFinished(() => mock.mockRestore());
  return { avds, qemuImg, directory, files, image };
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
  assert.equal(configuredProfile(config, profile, p.image), true);
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
  assert.equal(configuredProfile(config, profiles[0], p.image), true);
  for (const [key, value] of [
    ['hw.lcd.density', '640'],
    ['hw.initialOrientation', 'portrait'],
    ['abi.type', 'x86_64']
  ])
    assert.equal(
      configuredProfile({ ...config, [key]: value }, profiles[0], p.image),
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
  assert.equal(configuredProfile(rewritten, profile, p.image), true);
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

test('the one running Android device is found, whatever adb printed first', () => {
  assert.equal(
    onlyDevice(
      '* daemon started successfully\nList of devices attached\nemulator-5556\tdevice\n\n'
    ),
    'emulator-5556'
  );
});

test('no device, several devices or one not yet online is refused', () => {
  assert.throws(
    () => onlyDevice('List of devices attached\n\n'),
    /No Android device is online/
  );
  assert.throws(
    () =>
      onlyDevice(
        'List of devices attached\nemulator-5556\tdevice\nemulator-5558\toffline\n'
      ),
    /More than one Android device is running \(emulator-5556 device, emulator-5558 offline\)/
  );
  assert.throws(
    () => onlyDevice('List of devices attached\nemulator-5556\tunauthorized\n'),
    /'emulator-5556' is unauthorized.*adb kill-server/
  );
});

test('init permits phones but refuses running or offline emulators', () => {
  assertEmulatorsStopped(
    'List of devices attached\n0123ABC\tdevice\n192.168.1.9:5555\toffline\n'
  );
  for (const state of ['device', 'offline'])
    assert.throws(
      () =>
        assertEmulatorsStopped(
          `List of devices attached\n0123ABC\tdevice\nemulator-5556\t${state}\n`
        ),
      /Close every running Android emulator/
    );
});

test('explicit device selection never falls back to the connected phone', () => {
  const listing =
    'List of devices attached\nphone\tdevice\nemulator-5556\tdevice\nemulator-5558\toffline\n';
  assert.equal(onlyDevice(listing, 'emulator-5556'), 'emulator-5556');
  assert.throws(() => onlyDevice(listing, 'emulator-5558'), /is offline/);
  assert.throws(() => onlyDevice(listing, 'emulator-5560'), /is not connected/);
});

test('Windows SDK resolution handles spaces, overrides and executable suffixes', () => {
  const p = androidSdkPaths(
    { LOCALAPPDATA: 'C:\\Users\\Test User\\AppData\\Local' },
    'win32',
    'C:\\Users\\Test User'
  );
  assert.equal(
    p.adb,
    'C:\\Users\\Test User\\AppData\\Local\\Android\\Sdk\\platform-tools\\adb.exe'
  );
  assert.equal(p.avds, 'C:\\Users\\Test User\\.android\\avd');
  assert.ok(p.emulator.endsWith('emulator.exe'));
  assert.ok(p.qemuImg.endsWith('qemu-img.exe'));
  const custom = androidSdkPaths(
    {
      ANDROID_HOME: 'D:\\SDK',
      ADB: 'D:\\adb.exe',
      ANDROID_AVD_HOME: 'D:\\AVDs'
    },
    'win32',
    'C:\\User'
  );
  assert.equal(custom.adb, 'D:\\adb.exe');
  assert.equal(custom.avds, 'D:\\AVDs');
  assert.equal(custom.emulator, 'D:\\SDK\\emulator\\emulator.exe');
  assert.equal(
    androidSdkPaths({}, 'darwin', '/Users/test').adb,
    '/Users/test/Library/Android/sdk/platform-tools/adb'
  );
  assert.throws(
    () => hostImage('win32', 'arm64'),
    /supports macOS arm64 and Windows x64/
  );
});

test('Windows clones retain the translation image and its Android target', (t) => {
  const p = seed(t, 'userdata-qemu.img', hostImage('win32', 'x64'));
  assert.equal(localAvds(p)[0].seedable, true);
  cloneAvd(p, 'seed', profiles[0]);
  const config = parseIni(
    fs.readFileSync(
      path.join(p.avds, `${profiles[0].name}.avd/config.ini`),
      'utf8'
    )
  );
  const entry = parseIni(
    fs.readFileSync(path.join(p.avds, `${profiles[0].name}.ini`), 'utf8')
  );
  assert.equal(entry.target, 'android-35');
  assert.equal(configuredProfile(config, profiles[0], p.image), true);
  assert.equal(
    configuredProfile(config, profiles[0], hostImage('darwin', 'arm64')),
    false
  );
  assert.equal(reconcileAvd(p, profiles[0]), false);
});

test('ARM64 translation is required before provisioning an x86 device', () => {
  const runtime = {
    abiList: 'x86_64,x86,arm64-v8a',
    nativeBridge: 'libndk_translation.so',
    arm64Isa: 'x86_64'
  };
  const image = hostImage('win32', 'x64');
  assertArm64Runtime(image, runtime);
  assert.throws(
    () => assertArm64Runtime(image, { ...runtime, abiList: 'x86_64,x86' }),
    /arm64-v8a ABI/
  );
  for (const nativeBridge of ['', '0'])
    assert.throws(
      () => assertArm64Runtime(image, { ...runtime, nativeBridge }),
      /no active ARM64/
    );
  assert.throws(
    () => assertArm64Runtime(image, { ...runtime, arm64Isa: '' }),
    /no active ARM64/
  );
  assertArm64Runtime(hostImage('darwin', 'arm64'), {
    abiList: 'arm64-v8a',
    nativeBridge: '',
    arm64Isa: ''
  });
});

test('listed devices keep each state', () => {
  assert.deepEqual(
    listedDevices(
      'List of devices attached\nemulator-5556\tdevice\nR58M\tunauthorized\n'
    ),
    [
      { serial: 'emulator-5556', state: 'device' },
      { serial: 'R58M', state: 'unauthorized' }
    ]
  );
});
