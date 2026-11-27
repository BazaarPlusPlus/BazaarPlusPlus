import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import type { SpawnSyncOptions } from 'node:child_process';

export const ANDROID_DEV_APP = 'com.bazaarplusplus.android.dev';
export const ANDROID_LAUNCHER_ACTIVITY = `${ANDROID_DEV_APP}/com.bazaarplusplus.android.launch.LauncherActivity`;
// The game's external files directory inside the App's sandbox (GameStorage.root),
// relative to run-as's working directory: Payload paths are relative to it.
export const ANDROID_GAME_FILES = 'no_backup/game-external';
export const ANDROID_MELON_BASE = `${ANDROID_GAME_FILES}/MelonLoader`;
export const ANDROID_STAGING = '/data/local/tmp/bpp-staging';

export interface AdbDevice {
  serial: string;
  state: string;
}

export function androidSdkPaths(
  env: NodeJS.ProcessEnv = process.env,
  platform: NodeJS.Platform = process.platform,
  home = os.homedir()
) {
  const paths = platform === 'win32' ? path.win32 : path.posix;
  const sdk =
    env.ANDROID_HOME ||
    (platform === 'win32'
      ? paths.join(
          env.LOCALAPPDATA || paths.join(home, 'AppData/Local'),
          'Android/Sdk'
        )
      : paths.join(
          home,
          platform === 'darwin' ? 'Library/Android/sdk' : 'Android/Sdk'
        ));
  const executable = platform === 'win32' ? '.exe' : '';
  return {
    sdk,
    avds: env.ANDROID_AVD_HOME || paths.join(home, '.android/avd'),
    adb: env.ADB || paths.join(sdk, `platform-tools/adb${executable}`),
    emulator: paths.join(sdk, `emulator/emulator${executable}`),
    qemuImg: paths.join(sdk, `emulator/qemu-img${executable}`)
  };
}

export function resolveAdbBinary(
  env: NodeJS.ProcessEnv = process.env,
  platform: NodeJS.Platform = process.platform,
  home = os.homedir()
): string {
  if (env.ADB) return env.ADB;
  const pathEnv = env.PATH ?? '';
  const exe = platform === 'win32' ? 'adb.exe' : 'adb';
  for (const dir of pathEnv.split(path.delimiter)) {
    if (!dir) continue;
    const candidate = path.join(dir, exe);
    if (fs.statSync(candidate, { throwIfNoEntry: false })?.isFile()) {
      return candidate;
    }
  }
  const sdkAdb = androidSdkPaths(env, platform, home).adb;
  if (fs.statSync(sdkAdb, { throwIfNoEntry: false })?.isFile()) {
    return sdkAdb;
  }
  throw new Error(
    'adb not found. Put it on PATH, set ADB=/path/to/adb, or set ANDROID_HOME.'
  );
}

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

export function onlyDevice(listing: string, selected?: string): string {
  const all = listedDevices(listing);
  const listed = selected
    ? all.filter((device) => device.serial === selected)
    : all;
  if (selected && !listed.length)
    throw new Error(`Selected Android device '${selected}' is not connected.`);
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

export function adbEnv(
  env: NodeJS.ProcessEnv = process.env
): NodeJS.ProcessEnv {
  return { ...env, MSYS_NO_PATHCONV: '1' };
}

export function adbExec(
  adbBin: string,
  serial: string | null,
  args: readonly string[],
  options: Omit<SpawnSyncOptions, 'encoding'> & { allowFailure?: boolean } = {}
): { status: number; stdout: string; stderr: string } {
  const { allowFailure = false, env = process.env, ...rest } = options;
  const fullArgs = serial ? ['-s', serial, ...args] : [...args];
  const result = spawnSync(adbBin, fullArgs, {
    encoding: 'utf8',
    timeout: 60_000,
    windowsHide: true,
    ...rest,
    env: adbEnv(env)
  });
  const stdout = (result.stdout ?? '').replace(/\r+\n/g, '\n');
  const stderr = (result.stderr ?? '').replace(/\r+\n/g, '\n');
  const status = result.error ? 1 : (result.status ?? 1);
  if (!allowFailure && status !== 0) {
    throw new Error(
      `adb ${args.slice(0, 3).join(' ')} failed: ${result.error?.message || stderr.trim() || `exit ${status}`}`
    );
  }
  return { status, stdout, stderr };
}

export function adbExecBuffer(
  adbBin: string,
  serial: string,
  args: readonly string[],
  options: Omit<SpawnSyncOptions, 'encoding'> & { allowFailure?: boolean } = {}
): { status: number; stdout: Buffer; stderr: string } {
  const { allowFailure = false, env = process.env, ...rest } = options;
  const result = spawnSync(adbBin, ['-s', serial, ...args], {
    timeout: 60_000,
    maxBuffer: 64 * 1024 * 1024,
    windowsHide: true,
    ...rest,
    env: adbEnv(env)
  });
  const stdout = Buffer.isBuffer(result.stdout)
    ? result.stdout
    : Buffer.from(result.stdout ?? '');
  const stderr = Buffer.isBuffer(result.stderr)
    ? result.stderr.toString('utf8')
    : String(result.stderr ?? '');
  const status = result.error ? 1 : (result.status ?? 1);
  if (!allowFailure && status !== 0) {
    throw new Error(
      `adb ${args.slice(0, 3).join(' ')} failed: ${result.error?.message || stderr.trim() || `exit ${status}`}`
    );
  }
  return { status, stdout, stderr };
}

export function requireDevice(
  adbBin: string,
  selected?: string,
  env: NodeJS.ProcessEnv = process.env,
  appPackage = ANDROID_DEV_APP
): string {
  const listing = adbExec(adbBin, null, ['devices'], { env }).stdout;
  const serial = onlyDevice(
    listing,
    selected || env.ANDROID_SERIAL || undefined
  );
  const probe = adbExec(
    adbBin,
    serial,
    ['shell', 'run-as', appPackage, 'true'],
    {
      env,
      allowFailure: true
    }
  );
  if (probe.status !== 0) {
    throw new Error(
      `No debug Android App on '${serial}'; run 'just setup-device' for its AVD.`
    );
  }
  return serial;
}

export function appShell(
  adbBin: string,
  serial: string,
  command: string,
  env: NodeJS.ProcessEnv = process.env,
  allowFailure = false,
  appPackage = ANDROID_DEV_APP
): { status: number; stdout: string; stderr: string } {
  return adbExec(
    adbBin,
    serial,
    ['shell', `run-as ${appPackage} sh -c '${command}'`],
    { env, allowFailure }
  );
}

export function appCat(
  adbBin: string,
  serial: string,
  remotePath: string,
  env: NodeJS.ProcessEnv = process.env,
  allowFailure = false,
  appPackage = ANDROID_DEV_APP
): { status: number; stdout: Buffer; stderr: string } {
  return adbExecBuffer(
    adbBin,
    serial,
    ['exec-out', 'run-as', appPackage, 'cat', remotePath],
    { env, allowFailure }
  );
}

export function appPut(
  adbBin: string,
  serial: string,
  hostPath: string,
  remotePath: string,
  env: NodeJS.ProcessEnv = process.env,
  appPackage = ANDROID_DEV_APP
): void {
  const dir = path.posix.dirname(remotePath);
  adbExec(adbBin, serial, ['push', hostPath, ANDROID_STAGING], { env });
  try {
    appShell(
      adbBin,
      serial,
      `mkdir -p ${dir} && cp ${ANDROID_STAGING} ${remotePath}`,
      env,
      false,
      appPackage
    );
  } finally {
    adbExec(adbBin, serial, ['shell', 'rm', '-f', ANDROID_STAGING], {
      env,
      allowFailure: true
    });
  }
}
