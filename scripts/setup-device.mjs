import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { inspectScreen } from './android-screen.mjs';
import * as ios from './setup-device-ios.mjs';

const root = path.resolve(import.meta.dirname, '..');
const packageName = 'com.tempogames.thebazaar';
const melon = `/sdcard/Android/data/${packageName}/files/MelonLoader`;
const latestLog = `${melon}/MelonLoader/Latest.log`;
const activity = `${packageName}/com.unity3d.player.UnityPlayerActivity`;
const evidence = (profile) =>
  path.join(
    root,
    'bazaarplusplus-mod/artifacts/android',
    `setup-device-${profile.name}`
  );
// These exercise the unchanged, screen-fill-only and gathered-HUD branches of
// Core/Ui/ScreenFill. Dimensions are the game's landscape framebuffer, not wm overrides.
export const profiles = [
  { name: 'bpp_emu_20_9', width: 2400, height: 1080, dpi: 420, port: 5556 },
  { name: 'bpp_emu_3_2', width: 2400, height: 1600, dpi: 420, port: 5558 },
  { name: 'bpp_emu_fold', width: 2480, height: 2248, dpi: 420, port: 5560 }
];
const imagePath = 'system-images/android-35/google_apis_playstore/arm64-v8a/';
const help = `Local development matrices: Android AVDs and iOS simulators

  just list-devices

Android (macOS arm64, Node, Android SDK)

  just setup-device [NAME|all] [--seed AVD] [--timeout SECONDS] [--keep-open]

${profiles.map((p) => `  ${p.name.padEnd(17)} ${p.width}x${p.height}@${p.dpi}  emulator-${p.port}`).join('\n')}

init defaults to all, seed bpp_emu_20_9, and a 300-second login wait per AVD.
Missing AVDs copy the stopped seed's installed game and local account data;
existing AVDs keep their data. The seed must already have the injected game.
On a new machine, first install the LemonLoader-injected game on one Android 35
Google Play arm64 AVD and sign in, then use --seed NAME. No APK is downloaded.
Each AVD cold-boots, launches the game afresh, waits for MainMenuScene and a
following hooks summary, and captures the screen and native display metrics.
If sign-in is needed, complete it in the emulator window while init waits.
If the game exits during cold-start loading, init records it and retries once.
Success stops the AVD, preserving user data; --keep-open needs a single NAME.
Failure leaves the device open and returns nonzero. Rerun init after repair.
This prepares the installed build; it neither builds nor deploys this checkout.
All profiles are static screen fixtures; fold does not simulate a hinge.

ANDROID_HOME selects the SDK (default ~/Library/Android/sdk).
ANDROID_AVD_HOME selects AVDs (default ~/.android/avd); ADB overrides adb.
Evidence: bazaarplusplus-mod/artifacts/android/setup-device-NAME/ (private).
status inventories configuration and historical results, not current login.

iOS (macOS, Xcode, Node)

${ios.help}
`;

export function parseIni(text) {
  return Object.fromEntries(
    text.split(/\r?\n/).flatMap((line) => {
      const index = line.indexOf('=');
      return index > 0
        ? [[line.slice(0, index).trim(), line.slice(index + 1).trim()]]
        : [];
    })
  );
}

function writeIni(file, values) {
  fs.writeFileSync(
    file,
    Object.entries(values)
      .map(([key, value]) => `${key}=${value}\n`)
      .join(''),
    { mode: 0o600 }
  );
}

export function configuredProfile(config, profile) {
  return (
    config['image.sysdir.1'] === imagePath &&
    config['abi.type'] === 'arm64-v8a' &&
    Number(config['hw.lcd.width']) === profile.height &&
    Number(config['hw.lcd.height']) === profile.width &&
    Number(config['hw.lcd.density']) === profile.dpi &&
    config['hw.initialOrientation'] === 'landscape'
  );
}

export function menuReady(log) {
  const menu = log.search(
    /event=android\.scene\.loaded .*name=MainMenuScene\b/
  );
  return menu >= 0 && /event=android\.hooks\.summary\b/.test(log.slice(menu));
}

function run(binary, args, options = {}) {
  const result = spawnSync(binary, args, {
    encoding: 'utf8',
    timeout: 30_000,
    ...options
  });
  if (result.error || result.status !== 0)
    throw new Error(
      `${path.basename(binary)} ${args[0]} failed: ${result.error?.message || result.stderr?.trim() || `exit ${result.status}`}`
    );
  return result.stdout;
}

function paths() {
  const sdk =
    process.env.ANDROID_HOME || path.join(os.homedir(), 'Library/Android/sdk');
  return {
    sdk,
    avds:
      process.env.ANDROID_AVD_HOME || path.join(os.homedir(), '.android/avd'),
    adb: process.env.ADB || path.join(sdk, 'platform-tools/adb'),
    emulator: path.join(sdk, 'emulator/emulator'),
    qemuImg: path.join(sdk, 'emulator/qemu-img')
  };
}

const readConfig = (directory) =>
  parseIni(fs.readFileSync(path.join(directory, 'config.ini'), 'utf8'));

function avdDirectory(avds, name) {
  const entry = path.join(avds, `${name}.ini`);
  if (!fs.existsSync(entry)) return null;
  const directory = parseIni(fs.readFileSync(entry, 'utf8')).path;
  if (!directory || !path.isAbsolute(directory))
    throw new Error(`No absolute path in ${entry}`);
  return directory;
}

function devices(p) {
  return run(p.adb, ['devices'])
    .split(/\r?\n/)
    .filter((line) => /^emulator-\d+\s/.test(line))
    .map((line) => {
      const [serial, state] = line.trim().split(/\s+/);
      if (state !== 'device')
        throw new Error(
          `${serial} is ${state}; wait for it or close it first.`
        );
      return {
        serial,
        name: run(p.adb, ['-s', serial, 'emu', 'avd', 'name'])
          .trim()
          .split(/\r?\n/)[0]
      };
    });
}

// Flatten each active disk chain. The emulator may recreate a copied qcow2 overlay
// on a new AVD; a standalone base keeps the game and account data when that happens.
export function cloneAvd(p, seed, profile) {
  const source = avdDirectory(p.avds, seed);
  if (!source)
    throw new Error(
      `Seed AVD ${seed} is missing. See just setup-device --help.`
    );
  const target = path.join(p.avds, `${profile.name}.avd`);
  if (fs.existsSync(target) || avdDirectory(p.avds, profile.name))
    throw new Error(
      `AVD ${profile.name} already exists; it will not be overwritten.`
    );
  const config = readConfig(source);
  if (
    config['image.sysdir.1'] !== imagePath ||
    config['abi.type'] !== 'arm64-v8a'
  )
    throw new Error(`Seed ${seed} must use ${imagePath}`);
  const disks = fs
    .readdirSync(source)
    .filter((name) =>
      /^(userdata-qemu|encryptionkey|sdcard)\.img(\.qcow2)?$/.test(name)
    );
  if (
    !disks.includes('userdata-qemu.img') ||
    !disks.includes('encryptionkey.img')
  )
    throw new Error(
      `Seed ${seed} is missing its userdata or encryption key disk.`
    );
  // Keep the seed self-contained so provisioning cannot follow an unrelated disk.
  for (const disk of disks) {
    const info = JSON.parse(
      run(p.qemuImg, ['info', '--output=json', path.join(source, disk)])
    );
    const backing = info['backing-filename'];
    if (
      backing &&
      (!disks.includes(backing) || path.basename(backing) !== backing)
    )
      throw new Error(
        `${disk} has an external backing file; provision a self-contained seed first.`
      );
  }
  fs.mkdirSync(target, { mode: 0o700 });
  try {
    for (const disk of disks.filter((name) => name.endsWith('.img'))) {
      const active = disks.includes(`${disk}.qcow2`) ? `${disk}.qcow2` : disk;
      run(
        p.qemuImg,
        [
          'convert',
          '-O',
          'raw',
          path.join(source, active),
          path.join(target, disk)
        ],
        { timeout: 180_000 }
      );
      fs.chmodSync(path.join(target, disk), 0o600);
    }
    const values = {
      ...config,
      AvdId: profile.name,
      'avd.ini.displayname': profile.name,
      'hw.lcd.width': profile.height,
      'hw.lcd.height': profile.width,
      'hw.lcd.density': profile.dpi,
      'hw.initialOrientation': 'landscape',
      showDeviceFrame: 'no',
      'fastboot.forceColdBoot': 'yes',
      'fastboot.forceFastBoot': 'no',
      'userdata.useQcow2': 'yes'
    };
    for (const key of [
      'skin.path',
      'skin.name',
      'sdcard.path',
      'disk.dataPartition.path',
      'fastboot.chosenSnapshotFile'
    ])
      delete values[key];
    writeIni(path.join(target, 'config.ini'), values);
    writeIni(path.join(p.avds, `${profile.name}.ini`), {
      'avd.ini.encoding': 'UTF-8',
      path: target,
      target: 'android-35'
    });
  } catch (error) {
    fs.rmSync(target, { recursive: true, force: true });
    throw error;
  }
}

async function until(seconds, check, message) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (check()) return;
    await delay(2000);
  }
  throw new Error(message);
}

async function initialize(p, profile, { seed, timeout, keepOpen }) {
  const serial = `emulator-${profile.port}`;
  const active = devices(p);
  if (active.length)
    throw new Error(
      `Close running AVDs before init: ${active.map((d) => `${d.name} (${d.serial})`).join(', ')}. Init runs one device at a time.`
    );
  if (!avdDirectory(p.avds, profile.name)) {
    console.log(
      `Creating ${profile.name} from stopped seed ${seed} (local game/account data).`
    );
    cloneAvd(p, seed, profile);
  }
  const directory = avdDirectory(p.avds, profile.name);
  if (!configuredProfile(readConfig(directory), profile))
    throw new Error(
      `${profile.name} configuration differs from the matrix; inspect config.ini. Existing data was preserved.`
    );
  const out = evidence(profile);
  fs.mkdirSync(out, { recursive: true });
  const receipt = {
    name: profile.name,
    serial,
    startedAt: new Date().toISOString(),
    status: 'running'
  };
  const save = () =>
    fs.writeFileSync(
      path.join(out, 'init.json'),
      JSON.stringify(receipt, null, 2) + '\n'
    );
  save();
  const adb = (...args) => run(p.adb, ['-s', serial, ...args]);
  const launch = () => {
    adb('shell', 'rm', '-f', latestLog);
    adb('shell', 'am', 'start', '-n', activity);
  };
  const output = fs.openSync(path.join(out, 'emulator.log'), 'w', 0o600);
  const child = spawn(
    p.emulator,
    [
      '-avd',
      profile.name,
      '-port',
      String(profile.port),
      '-no-snapshot-load',
      '-no-boot-anim'
    ],
    {
      detached: true,
      stdio: ['ignore', output, output]
    }
  );
  let startError;
  child.on('error', (error) => {
    startError = error;
  });
  child.unref();
  fs.closeSync(output);
  try {
    console.log(
      `Booting ${profile.name}; login wait ${timeout}s. Complete game sign-in in its window if requested.`
    );
    await until(
      120,
      () => {
        if (startError) throw startError;
        if (child.exitCode !== null)
          throw new Error(`Emulator exited; see ${out}/emulator.log`);
        try {
          return adb('shell', 'getprop', 'sys.boot_completed').trim() === '1';
        } catch {
          return false;
        }
      },
      `Boot timed out; see ${out}/emulator.log`
    );
    if (adb('emu', 'avd', 'name').trim().split(/\r?\n/)[0] !== profile.name)
      throw new Error(
        `${serial} is not ${profile.name}; refusing to touch its game.`
      );
    try {
      adb('shell', 'test', '-f', `${melon}/Mods/BazaarPlusPlus.Android.dll`);
    } catch {
      throw new Error(
        `${profile.name} has no Android Mod Payload. Install the LemonLoader-injected game before rerunning init.`
      );
    }
    adb('shell', 'am', 'force-stop', packageName);
    adb('logcat', '-c');
    launch();
    let launchedAt = Date.now();
    receipt.restarts = 0;
    await until(
      timeout,
      () => {
        let log = '';
        try {
          log = adb('shell', 'cat', latestLog);
        } catch {
          /* The loader has not created its log yet. */
        }
        if (menuReady(log)) return true;
        // Cold boot has killed the first game process before loading the mod on
        // this matrix. Give am start time to spawn, retain evidence, retry once.
        if (Date.now() - launchedAt > 10_000) {
          let running = false;
          try {
            running = Boolean(adb('shell', 'pidof', packageName).trim());
          } catch {
            /* pidof exits 1 when absent. */
          }
          if (!running) {
            if (receipt.restarts)
              throw new Error(
                `Game exited twice before the main menu on ${profile.name}; inspect ${out}.`
              );
            fs.writeFileSync(path.join(out, 'first-launch.log'), log, {
              mode: 0o600
            });
            receipt.restarts = 1;
            save();
            console.log(
              `${profile.name}: game exited before the main menu; retrying once (first-launch.log retained).`
            );
            launch();
            launchedAt = Date.now();
          }
        }
        return false;
      },
      `Game login/main menu not ready on ${profile.name}; finish sign-in in the open emulator, close it, then rerun init.`
    );
    run('just', ['mod::android-screen-check', `setup-device-${profile.name}`], {
      cwd: root,
      env: { ...process.env, ADB: p.adb, BPP_ANDROID_SERIAL: serial }
    });
    const screen = inspectScreen(out, serial);
    if (
      screen.screenshot.width !== profile.width ||
      screen.screenshot.height !== profile.height ||
      !new RegExp(`Physical density: ${profile.dpi}(?:\\s|$)`).test(
        screen.density
      )
    )
      throw new Error(
        `${profile.name} actual display differs from the matrix; see ${out}/environment.json`
      );
    receipt.status = 'ready';
    receipt.checkedAt = new Date().toISOString();
    receipt.screen = screen;
    save();
    if (!keepOpen) {
      adb('shell', 'am', 'force-stop', packageName);
      adb('emu', 'kill');
      await until(
        60,
        () => child.exitCode !== null,
        `Emulator is still shutting down: ${profile.name}`
      );
    }
    console.log(
      `READY ${profile.name}: fresh main menu and ${profile.width}x${profile.height}@${profile.dpi}; ${out}/init.json`
    );
  } catch (error) {
    receipt.status = 'failed';
    receipt.error = error.message;
    save();
    // Preserve a diagnostic screenshot even when login never reaches MainMenuScene.
    try {
      fs.writeFileSync(
        path.join(out, 'screen.png'),
        run(p.adb, ['-s', serial, 'exec-out', 'screencap', '-p'], {
          encoding: null
        })
      );
    } catch {
      /* Device may not have booted. */
    }
    throw error;
  }
}

export async function main(args) {
  if (args.includes('--help') || args.includes('-h')) {
    console.log(help);
    return;
  }
  const [command, ...rest] = args;
  const p = paths();
  if (command === 'init' && rest[0] === 'ios') return ios.init(rest.slice(1));
  if (command === 'status' && !rest.length) {
    for (const profile of profiles) {
      const directory = avdDirectory(p.avds, profile.name);
      const state = !directory
        ? 'missing'
        : configuredProfile(readConfig(directory), profile)
          ? 'configured'
          : 'config mismatch';
      const receipt = path.join(evidence(profile), 'init.json');
      const previous = fs.existsSync(receipt)
        ? JSON.parse(fs.readFileSync(receipt, 'utf8'))
        : null;
      console.log(
        `${profile.name}: ${state}; last init ${previous?.status || 'not run'}${previous?.checkedAt ? ` at ${previous.checkedAt}` : ''} (current login unverified)`
      );
    }
    ios.status();
    return;
  }
  if (command !== 'init') throw new Error(help);
  let name = 'all',
    seed = profiles[0].name,
    timeout = 300,
    keepOpen = false;
  if (rest[0] && !rest[0].startsWith('--')) name = rest.shift();
  while (rest.length) {
    const flag = rest.shift();
    if (flag === '--seed') seed = rest.shift();
    else if (flag === '--timeout') timeout = Number(rest.shift());
    else if (flag === '--keep-open') keepOpen = true;
    else throw new Error(`Unknown option ${flag}; see --help.`);
  }
  if (
    !seed ||
    !/^[A-Za-z0-9_.-]+$/.test(seed) ||
    !Number.isFinite(timeout) ||
    timeout <= 0
  )
    throw new Error(
      'Provide a valid seed AVD name and positive --timeout seconds.'
    );
  const selected =
    name === 'all'
      ? profiles
      : profiles.filter((profile) => profile.name === name);
  if (!selected.length || (keepOpen && selected.length !== 1))
    throw new Error(
      'Choose a matrix NAME; --keep-open requires a single NAME. See --help.'
    );
  if (process.platform !== 'darwin' || process.arch !== 'arm64')
    throw new Error(
      'This arm64 emulator matrix requires an Apple Silicon Mac.'
    );
  for (const profile of selected)
    await initialize(p, profile, { seed, timeout, keepOpen });
}

if (import.meta.main) {
  try {
    await main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
