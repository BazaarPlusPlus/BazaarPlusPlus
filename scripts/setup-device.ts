import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import type { SpawnSyncOptions, SpawnSyncReturns } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import {
  fetchLockedAndroidInput,
  lockedInteropEntry
} from '../release/game-libs.ts';
import { signIn, withLoginCancellation } from './android-login.ts';
import { inspectScreen } from './android-screen.ts';
import type { LoginReceipt } from './android-login.ts';
import type { ScreenReport } from './android-screen.ts';

/** One AVD of the device matrix: its landscape screen and emulator port. */
export interface DeviceProfile {
  name: string;
  width: number;
  height: number;
  dpi: number;
  port: number;
}

/** The Android SDK tools and AVD directory this machine uses. */
export interface SdkPaths {
  sdk: string;
  avds: string;
  adb: string;
  emulator: string;
  qemuImg: string;
}

/** One `adb devices` row. */
export interface AdbDevice {
  serial: string;
  state: string;
}

/** init.json: one init attempt of one AVD and what it found. */
export interface InitReceipt {
  name: string;
  serial: string;
  startedAt: string;
  status: 'running' | 'ready' | 'failed';
  login?: LoginReceipt;
  checkedAt?: string;
  screen?: ScreenReport;
  error?: string;
}

// The environment provisioning gives recipes: this SDK's adb and the device.
interface DeviceEnv extends NodeJS.ProcessEnv {
  ADB: string;
  ANDROID_SERIAL: string;
}

interface InitOptions {
  seed: string;
  timeout: number;
  keepOpen: boolean;
  recreate: boolean;
}

// run's options; the output is text unless encoding is null.
interface RunOptions extends Omit<SpawnSyncOptions, 'encoding'> {
  encoding?: BufferEncoding;
}

const root = path.resolve(import.meta.dirname, '..');
const gamePackage = 'com.tempogames.thebazaar';
// The debug Android App launches the game as a player does; the game runs in its process.
const app = 'com.bazaarplusplus.android.dev';
// Relative to run-as's working directory: the game's files in the App's sandbox (ADR 0012).
const latestLog = 'no_backup/game-external/MelonLoader/MelonLoader/Latest.log';
// The App's LemonLoader reset marker: the debug App's versionCode never changes, so a reinstall
// alone would keep an older Loader Release's deployment.
const loaderDeployment = 'no_backup/loader-deployment.json';
const androidApp = path.join(root, 'bazaarplusplus-android');
const evidence = (profile: DeviceProfile): string =>
  path.join(
    root,
    'bazaarplusplus-mod/artifacts/android',
    `setup-device-${profile.name}`
  );
// These exercise the unchanged, screen-fill-only and gathered-HUD branches of
// Core/Ui/ScreenFill. Dimensions are the game's landscape framebuffer, not wm overrides.
export const profiles: DeviceProfile[] = [
  { name: 'bpp_emu_20_9', width: 2400, height: 1080, dpi: 420, port: 5556 },
  { name: 'bpp_emu_3_2', width: 2400, height: 1600, dpi: 420, port: 5558 },
  { name: 'bpp_emu_fold', width: 2480, height: 2248, dpi: 420, port: 5560 }
];
const imagePath = 'system-images/android-35/google_apis_playstore/arm64-v8a/';
const help = `Local development matrix: Android AVDs

  just list-devices

Android (macOS arm64, Node, Android SDK)

  just setup-device [NAME|all] [--seed AVD] [--timeout SECONDS] [--keep-open]
  just setup-device NAME --recreate [--seed AVD]

${profiles.map((p) => `  ${p.name.padEnd(17)} ${p.width}x${p.height}@${p.dpi}  emulator-${p.port}`).join('\n')}

init defaults to all, seed bpp_emu_20_9, and a 300-second login wait per AVD.
Missing AVDs copy the stopped seed's installed apps and local data. An existing
AVD keeps its data: init rewrites its screen settings to the matrix. An AVD on
another system image stops init; --recreate deletes it with its data and copies
the seed again (not the seed itself). Any Android 35 Google Play arm64 AVD can
be the seed, whatever its name; init never renames or deletes an AVD outside
the matrix, and status lists them.
Each AVD runs the game as a player does: init installs the locked official
game when another build is there (uninstalling it loses only that package's own
data; the App's sandbox and its game login stay), installs the
debug Android App with its runtime and the Development interop, deploys this
checkout's mod into the App, then starts one development login request. With
config.ini [game], the Mod submits the game's own login form; without it, sign
in in the game window. It waits for authenticated login and profile data in that
process, then MainMenuScene and a following hooks summary, and captures the
screen and native display metrics. Device or game failures stop this attempt.
Success stops the AVD, preserving user data; --keep-open needs a single NAME.
Failure leaves the device open and returns nonzero. Rerun init after repair.
The official APK, build/runtime and build/interop.zip come from the private
store once: just with-config release just setup-device NAME, and
just with-config release just android::stage-runtime (and ::interop-package).
All profiles are static screen fixtures; fold does not simulate a hinge.

ANDROID_HOME selects the SDK (default ~/Library/Android/sdk).
ANDROID_AVD_HOME selects AVDs (default ~/.android/avd); ADB overrides adb.
Evidence: bazaarplusplus-mod/artifacts/android/setup-device-NAME/ (private).
status inventories configuration and historical results, not current login.
`;

export function parseIni(text: string): Record<string, string> {
  return Object.fromEntries(
    text.split(/\r?\n/).flatMap((line): [string, string][] => {
      const index = line.indexOf('=');
      return index > 0
        ? [[line.slice(0, index).trim(), line.slice(index + 1).trim()]]
        : [];
    })
  );
}

function writeIni(file: string, values: Record<string, string | number>): void {
  fs.writeFileSync(
    file,
    Object.entries(values)
      .map(([key, value]) => `${key}=${value}\n`)
      .join(''),
    { mode: 0o600 }
  );
}

const matrixImage = (config: Record<string, string>): boolean =>
  config['image.sysdir.1'] === imagePath && config['abi.type'] === 'arm64-v8a';

// The matrix owns these screen keys on every AVD. None of them touches a disk, so init
// rewrites them in place on an existing AVD and its game and login stay.
const screenConfig = (profile: DeviceProfile) => ({
  'hw.lcd.width': profile.height,
  'hw.lcd.height': profile.width,
  'hw.lcd.density': profile.dpi,
  'hw.initialOrientation': 'landscape',
  showDeviceFrame: 'no'
});
// A skin can set its own screen size.
const skinKeys = ['skin.path', 'skin.name'];

export function configuredProfile(
  config: Record<string, string>,
  profile: DeviceProfile
): boolean {
  return (
    matrixImage(config) &&
    Number(config['hw.lcd.width']) === profile.height &&
    Number(config['hw.lcd.height']) === profile.width &&
    Number(config['hw.lcd.density']) === profile.dpi &&
    config['hw.initialOrientation'] === 'landscape'
  );
}

// Brings an existing AVD to the matrix. Another system image cannot be rewritten: the
// userdata disk belongs to its image, so that AVD needs --recreate.
export function reconcileAvd(
  p: Pick<SdkPaths, 'avds'>,
  profile: DeviceProfile
): boolean {
  const directory = avdDirectory(p.avds, profile.name);
  // Every caller creates the AVD first.
  if (!directory) throw new Error(`AVD ${profile.name} does not exist`);
  const config = readConfig(directory);
  if (!matrixImage(config))
    throw new Error(
      `${profile.name} uses another system image than ${imagePath}. Rerun with: just setup-device ${profile.name} --recreate (it deletes the AVD and its data).`
    );
  if (configuredProfile(config, profile)) return false;
  const values: Record<string, string | number> = {
    ...config,
    ...screenConfig(profile)
  };
  for (const key of skinKeys) delete values[key];
  writeIni(path.join(directory, 'config.ini'), values);
  return true;
}

// Deletes only the layout cloneAvd writes, never a directory another .ini points to.
export function removeAvd(p: Pick<SdkPaths, 'avds'>, name: string): void {
  const target = path.join(p.avds, `${name}.avd`);
  const directory = avdDirectory(p.avds, name);
  if (directory && path.resolve(directory) !== target)
    throw new Error(
      `${name}.ini points to ${directory}, not ${target}; delete that AVD by hand.`
    );
  fs.rmSync(target, { recursive: true, force: true });
  fs.rmSync(path.join(p.avds, `${name}.ini`), { force: true });
}

export function menuReady(log: string): boolean {
  const menu = log.search(
    /event=android\.scene\.loaded .*name=MainMenuScene\b/
  );
  return menu >= 0 && /event=android\.hooks\.summary\b/.test(log.slice(menu));
}

function run(
  binary: string,
  args: string[],
  options: Omit<SpawnSyncOptions, 'encoding'> & { encoding: null }
): Buffer;
function run(binary: string, args: string[], options?: RunOptions): string;
function run(
  binary: string,
  args: string[],
  options: SpawnSyncOptions = {}
): string | Buffer {
  // Text unless the caller asked for bytes; the failure message reads stderr
  // as text either way.
  const result = spawnSync(binary, args, {
    encoding: 'utf8',
    timeout: 30_000,
    ...options
  }) as SpawnSyncReturns<string>;
  if (result.error || result.status !== 0)
    throw new Error(
      `${path.basename(binary)} ${args[0]} failed: ${result.error?.message || result.stderr?.trim() || `exit ${result.status}`}`
    );
  return result.stdout;
}

function paths(): SdkPaths {
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

const readConfig = (directory: string): Record<string, string> =>
  parseIni(fs.readFileSync(path.join(directory, 'config.ini'), 'utf8'));

function avdDirectory(avds: string, name: string): string | null {
  const entry = path.join(avds, `${name}.ini`);
  if (!fs.existsSync(entry)) return null;
  const directory = parseIni(fs.readFileSync(entry, 'utf8')).path;
  if (!directory || !path.isAbsolute(directory))
    throw new Error(`No absolute path in ${entry}`);
  return directory;
}

// Every AVD here, by its .ini name, and whether it is on the matrix image and so can be the seed.
export function localAvds(
  p: Pick<SdkPaths, 'avds'>
): { name: string; seedable: boolean }[] {
  if (!fs.existsSync(p.avds)) return [];
  return fs
    .readdirSync(p.avds)
    .filter((file) => file.endsWith('.ini'))
    .map((file) => {
      const name = file.slice(0, -'.ini'.length);
      let seedable = false;
      try {
        // The .ini exists, so avdDirectory returns its path or throws.
        const directory = avdDirectory(p.avds, name);
        if (!directory) throw new Error(`No ${name}.ini`);
        seedable = matrixImage(readConfig(directory));
      } catch {
        /* A broken AVD cannot be the seed. */
      }
      return { name, seedable };
    })
    .sort((a, b) => a.name.localeCompare(b.name));
}

function missingSeed(
  p: Pick<SdkPaths, 'avds'>,
  seed: string,
  profile: DeviceProfile
): string {
  const candidates = localAvds(p)
    .filter((avd) => avd.seedable && avd.name !== profile.name)
    .map((avd) => avd.name);
  return candidates.length
    ? `Seed AVD ${seed} is missing. These AVDs can be the seed: ${candidates.join(', ')}. Rerun init with --seed ${candidates[0]}.`
    : `Seed AVD ${seed} is missing and no AVD here uses ${imagePath}. Create one (Android 35, Google Play, arm64) in Android Studio, then rerun with --seed NAME.`;
}

// Every device in `adb devices` output, whatever its state and whatever adb printed before the list.
// One Android device runs at a time (docs/development.md); bazaarplusplus-mod/scripts/android.sh
// applies the same rule in Bash (find_device).
export function listedDevices(listing: string): AdbDevice[] {
  const lines = listing.split(/\r?\n/);
  return lines
    .slice(lines.findIndex((line) => line.startsWith('List of devices')) + 1)
    .map((line) => line.trim().split(/\s+/))
    .filter((fields) => fields.length >= 2)
    .map(([serial, state]) => ({ serial, state }));
}

const listedText = (listed: AdbDevice[]): string =>
  listed.map((d) => `${d.serial} ${d.state}`).join(', ');

// The serial of the one running device; none, several or one not yet online is refused.
export function onlyDevice(listing: string): string {
  const listed = listedDevices(listing);
  if (!listed.length)
    throw new Error(
      'No Android device is online; open one AVD with: just setup-device NAME --keep-open'
    );
  if (listed.length > 1)
    throw new Error(
      `More than one Android device is running (${listedText(listed)}); close all but one.`
    );
  const [{ serial, state }] = listed;
  if (state !== 'device')
    throw new Error(
      `Device '${serial}' is ${state}; wait for it or close it (a stale entry with no emulator running clears with: adb kill-server).`
    );
  return serial;
}

// Flatten each active disk chain. The emulator may recreate a copied qcow2 overlay
// on a new AVD; a standalone base keeps the game and account data when that happens.
export function cloneAvd(
  p: Pick<SdkPaths, 'avds' | 'qemuImg'>,
  seed: string,
  profile: DeviceProfile
): void {
  const source = avdDirectory(p.avds, seed);
  if (!source) throw new Error(missingSeed(p, seed, profile));
  const target = path.join(p.avds, `${profile.name}.avd`);
  if (fs.existsSync(target) || avdDirectory(p.avds, profile.name))
    throw new Error(
      `AVD ${profile.name} already exists; it will not be overwritten.`
    );
  const config = readConfig(source);
  if (!matrixImage(config))
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
    ) as { 'backing-filename'?: string };
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
    const values: Record<string, string | number> = {
      ...config,
      AvdId: profile.name,
      'avd.ini.displayname': profile.name,
      ...screenConfig(profile),
      'fastboot.forceColdBoot': 'yes',
      'fastboot.forceFastBoot': 'no',
      'userdata.useQcow2': 'yes'
    };
    for (const key of [
      ...skinKeys,
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

async function until(
  seconds: number,
  check: () => boolean,
  message: string
): Promise<void> {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (check()) return;
    await delay(2000);
  }
  throw new Error(message);
}

// Brings the AVD to the player's setup with this checkout: the locked official game, the debug
// Android App with its runtime and the Development interop, this checkout's mod in the App's game
// files, and the game login from config.ini [game] when it holds one.
async function provision(
  adb: (...args: string[]) => string,
  env: DeviceEnv,
  profile: DeviceProfile
): Promise<void> {
  const locked = lockedInteropEntry(root).apkSha256;
  let installed = '';
  try {
    const apk = adb('shell', 'pm', 'path', gamePackage)
      .split(/\r?\n/)
      .find((line) => line.endsWith('/base.apk'))
      ?.slice('package:'.length);
    // Hashing the 2.8 GB APK on the emulator takes far longer than an adb call.
    if (apk)
      installed = run(
        env.ADB,
        ['-s', env.ANDROID_SERIAL, 'shell', 'sha256sum', apk],
        { timeout: 300_000 }
      )
        .trim()
        .split(/\s+/)[0];
  } catch {
    /* pm path exits 1 when the game is not installed. */
  }
  if (installed !== locked) {
    let file: string;
    try {
      file = await fetchLockedAndroidInput({ kind: 'apk', log: console.log });
    } catch (error) {
      throw new Error(
        `The locked official game APK is not cached (${(error as Error).message}). Fetch it once with: just with-config release just setup-device ${profile.name}`
      );
    }
    console.log(
      `${profile.name}: installing the locked official game (${installed ? 'replacing another build' : 'not installed'}).`
    );
    if (installed) adb('uninstall', gamePackage);
    run(env.ADB, ['-s', env.ANDROID_SERIAL, 'install', file], {
      timeout: 900_000
    });
  }
  for (const [file, recipe] of [
    ['build/runtime', 'android::stage-runtime'],
    ['build/interop.zip', 'android::interop-package']
  ])
    if (!fs.existsSync(path.join(androidApp, file)))
      throw new Error(
        `No ${file} in bazaarplusplus-android; run: just with-config release just ${recipe}`
      );
  const just = (...args: string[]) =>
    run('just', args, { cwd: root, env, timeout: 900_000 });
  console.log(
    `${profile.name}: installing the debug App, the Development interop and this checkout's mod.`
  );
  just('android::install');
  adb('shell', 'run-as', app, 'rm', '-f', loaderDeployment);
  just('android::push-interop');
  just('mod::android-build');
  just('mod::android-deploy');
}

async function initialize(
  p: SdkPaths,
  profile: DeviceProfile,
  { seed, timeout, keepOpen, recreate }: InitOptions
): Promise<void> {
  const serial = `emulator-${profile.port}`;
  const active = listedDevices(run(p.adb, ['devices']));
  if (active.length)
    throw new Error(
      `Close every running Android device before init: ${listedText(active)}. One device runs at a time.`
    );
  if (recreate && avdDirectory(p.avds, profile.name)) {
    console.log(`Deleting ${profile.name} and its data (--recreate).`);
    removeAvd(p, profile.name);
  }
  if (!avdDirectory(p.avds, profile.name)) {
    console.log(
      `Creating ${profile.name} from stopped seed ${seed} (local game/account data).`
    );
    cloneAvd(p, seed, profile);
  }
  if (reconcileAvd(p, profile))
    console.log(
      `${profile.name}: rewrote its screen settings to the matrix; its data stays.`
    );
  const out = evidence(profile);
  fs.mkdirSync(out, { recursive: true });
  const receipt: InitReceipt = {
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
  const adb = (...args: string[]) => run(p.adb, ['-s', serial, ...args]);
  // Recipes and android-login call bare adb and Gradle: give them this SDK.
  const env: DeviceEnv = {
    ...process.env,
    ADB: p.adb,
    ANDROID_HOME: p.sdk,
    ANDROID_SERIAL: serial,
    PATH: `${path.dirname(p.adb)}${path.delimiter}${process.env.PATH}`
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
  let startError: Error | undefined;
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
    await provision(adb, env, profile);
    const login = await withLoginCancellation((signal) =>
      signIn(serial, {
        env,
        timeout,
        allowManual: true,
        onProgress: console.log,
        signal
      })
    );
    receipt.login = login;
    save();
    await until(
      60,
      () => {
        const pid = adb('shell', `pidof ${app} || true`).trim();
        if (pid !== String(login.pid))
          throw new Error('The authenticated game process exited.');
        try {
          return menuReady(adb('exec-out', 'run-as', app, 'cat', latestLog));
        } catch {
          return false;
        }
      },
      `The authenticated game has no fresh main-menu/hooks evidence; inspect ${out}.`
    );
    run('just', ['mod::android-screen-check', `setup-device-${profile.name}`], {
      cwd: root,
      env: { ...process.env, ADB: p.adb }
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
      adb('shell', 'am', 'force-stop', app);
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
    receipt.error = (error as Error).message;
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

export async function main(args: string[]): Promise<void> {
  if (args.includes('--help') || args.includes('-h')) {
    console.log(help);
    return;
  }
  const [command, ...rest] = args;
  const p = paths();
  if (command === 'status' && !rest.length) {
    for (const profile of profiles) {
      const directory = avdDirectory(p.avds, profile.name);
      const config = directory && readConfig(directory);
      const state = !config
        ? 'missing; init copies the seed'
        : !matrixImage(config)
          ? 'other system image; init needs --recreate'
          : configuredProfile(config, profile)
            ? 'configured'
            : 'screen differs; init rewrites it';
      const receipt = path.join(evidence(profile), 'init.json');
      const previous = fs.existsSync(receipt)
        ? (JSON.parse(fs.readFileSync(receipt, 'utf8')) as InitReceipt)
        : null;
      console.log(
        `${profile.name}: ${state}; last init ${previous?.status || 'not run'}${previous?.checkedAt ? ` at ${previous.checkedAt}` : ''} (current login unverified)`
      );
    }
    for (const avd of localAvds(p).filter(
      (avd) => !profiles.some((profile) => profile.name === avd.name)
    ))
      console.log(
        `${avd.name}: not in the matrix; ${avd.seedable ? 'can be the --seed' : 'other system image, cannot be the seed'}`
      );
    return;
  }
  if (command !== 'init') throw new Error(help);
  let name: string | undefined = 'all',
    seed: string | undefined = profiles[0].name,
    timeout = 300,
    keepOpen = false,
    recreate = false;
  if (rest[0] && !rest[0].startsWith('--')) name = rest.shift();
  while (rest.length) {
    const flag = rest.shift();
    if (flag === '--seed') seed = rest.shift();
    else if (flag === '--timeout') timeout = Number(rest.shift());
    else if (flag === '--keep-open') keepOpen = true;
    else if (flag === '--recreate') recreate = true;
    else throw new Error(`Unknown option ${flag}; see --help.`);
  }
  if (
    !seed ||
    !/^[A-Za-z0-9_.-]+$/.test(seed) ||
    !Number.isFinite(timeout) ||
    timeout < 1 ||
    timeout > 600
  )
    throw new Error(
      'Provide a valid seed AVD name and --timeout between 1 and 600 seconds.'
    );
  const selected =
    name === 'all'
      ? profiles
      : profiles.filter((profile) => profile.name === name);
  if (!selected.length || ((keepOpen || recreate) && selected.length !== 1))
    throw new Error(
      'Choose a matrix NAME; --keep-open and --recreate require a single NAME. See --help.'
    );
  if (recreate && name === seed)
    throw new Error(
      `${name} is the seed; --recreate copies the seed, so choose another with --seed.`
    );
  if (process.platform !== 'darwin' || process.arch !== 'arm64')
    throw new Error(
      'This arm64 emulator matrix requires an Apple Silicon Mac.'
    );
  for (const profile of selected)
    await initialize(p, profile, { seed, timeout, keepOpen, recreate });
}

if (import.meta.main) {
  try {
    await main(process.argv.slice(2));
  } catch (error) {
    console.error((error as NodeJS.ErrnoException).message);
    process.exitCode =
      (error as NodeJS.ErrnoException).code === 'LOGIN_CANCELLED' ? 130 : 1;
  }
}
