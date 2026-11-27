/**
 * Development only: puts sampled cloud runs into a Data Root's history
 * database, or takes them out again, so History can be checked on desktop or
 * Android without playing. Entry point: `just history-sample`.
 */
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import {
  MOD_ROOT,
  WORKSPACE_ROOT,
  desktopDataRoot,
  dotnetEnv,
  err,
  gameRoot,
  hostPlatform,
  ok
} from '../../bazaarplusplus-mod/scripts/mod-env.ts';
import {
  ANDROID_DEV_APP,
  ANDROID_GAME_FILES,
  adbExec,
  appCat,
  appPut,
  appShell,
  requireDevice,
  resolveAdbBinary
} from '../android/adb.ts';

const SAMPLER_PROJECT = path.join(
  MOD_ROOT,
  'tools/HistorySampler/HistorySampler.csproj'
);
const DATA_ROOT_NAME = 'BazaarPlusPlusV6';
const DATABASE = 'bazaarplusplus.db';
const ANALYZER_ENV = path.join(
  WORKSPACE_ROOT,
  'bazaarplusplus-analyzer',
  '.env'
);

const USAGE = `Usage: just history-sample <desktop|android> [import|clear] [tool options]

  import (default)  copy recent cloud runs in as the local player's History-Only Runs
  clear             remove every sampled run, its battles, favorites, videos and replays

desktop writes the installed game's Data Root (BPP_GAME_ROOT or the discovered game); quit the game
first. android uses ANDROID_SERIAL or the only connected device: it stops the debug App, copies the
database out, writes it here, and copies it and the replay files back. Start the game again
afterwards (just mod::android-launch). The cloud feed needs the bundle_sync token in
BPP_BUNDLE_SYNC_TOKEN: \`just history-sample\` supplies it, or pass --bundle FILE.
Tool options (--count, --hours, --bundle, --account, --api):
dotnet run --project bazaarplusplus-mod/tools/HistorySampler -- --help`;

export function dotenvValue(text: string, key: string): string | undefined {
  for (const line of text.split(/\r?\n/)) {
    const match = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(line);
    if (match?.[1] !== key) continue;
    const value = match[2].trim().replace(/^(['"])(.*)\1$/, '$2');
    return value.length > 0 ? value : undefined;
  }
  return undefined;
}

function samplerEnv(env: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  const next = dotnetEnv(env);
  if (!next.BPP_BUNDLE_SYNC_TOKEN && fs.existsSync(ANALYZER_ENV)) {
    const token = dotenvValue(
      fs.readFileSync(ANALYZER_ENV, 'utf8'),
      'BPP_BUNDLE_SYNC_TOKEN'
    );
    if (token) next.BPP_BUNDLE_SYNC_TOKEN = token;
  }
  return next;
}

function runSampler(args: readonly string[], env: NodeJS.ProcessEnv): void {
  const result = spawnSync(
    'dotnet',
    ['run', '--project', SAMPLER_PROJECT, '-v', 'q', '--', ...args],
    {
      cwd: MOD_ROOT,
      env: samplerEnv(env),
      stdio: 'inherit'
    }
  );
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(
      `HistorySampler failed with exit code ${result.status ?? 1}`
    );
  }
}

function assertDesktopGameClosed(gameDir: string): void {
  if (hostPlatform() === 'windows') {
    const exe = path.join(gameDir, 'TheBazaar.exe');
    const result = spawnSync(
      'powershell.exe',
      [
        '-NoProfile',
        '-Command',
        '$ErrorActionPreference = "Stop"; foreach ($p in (Get-CimInstance Win32_Process | Where-Object Name -eq "TheBazaar.exe")) { if (!$p.ExecutablePath -or $p.ExecutablePath -eq $env:BPP_SAMPLE_EXE) { exit 1 } }'
      ],
      {
        env: { ...process.env, BPP_SAMPLE_EXE: exe },
        stdio: 'ignore'
      }
    );
    if (result.status !== 0) {
      throw new Error('Cannot confirm the selected game is closed.');
    }
    return;
  }
  const result = spawnSync(
    'pgrep',
    ['-f', 'TheBazaar(_ARM64)?[.]app/Contents/MacOS'],
    { stdio: 'ignore' }
  );
  if (result.status === 0) {
    throw new Error(
      'The game is running; quit it first, so the mod is not writing the same database.'
    );
  }
}

function sampleDesktop(
  command: string,
  extraArgs: readonly string[],
  env: NodeJS.ProcessEnv
): void {
  const game = gameRoot(env);
  if (!game || !fs.statSync(game, { throwIfNoEntry: false })?.isDirectory()) {
    throw new Error('Cannot locate the game directory.');
  }
  assertDesktopGameClosed(game);
  const dataRoot = desktopDataRoot(game);
  if (
    !fs
      .statSync(path.join(dataRoot, DATABASE), { throwIfNoEntry: false })
      ?.isFile()
  ) {
    throw new Error(
      `No history database at ${dataRoot}; start the game with the mod once.`
    );
  }
  runSampler(
    [command, '--data-root', dataRoot, '--journal', 'wal', ...extraArgs],
    env
  );
}

function sampleAndroid(
  command: string,
  extraArgs: readonly string[],
  env: NodeJS.ProcessEnv
): void {
  const adb = resolveAdbBinary(env);
  const serial = requireDevice(adb, env.ANDROID_SERIAL, env);
  const remote = `${ANDROID_GAME_FILES}/${DATA_ROOT_NAME}`;
  const work = path.join(MOD_ROOT, 'artifacts/history-sample', serial);
  fs.rmSync(work, { recursive: true, force: true });
  fs.mkdirSync(work, { recursive: true });

  adbExec(adb, serial, ['shell', 'am', 'force-stop', ANDROID_DEV_APP], { env });
  const dbCat = appCat(adb, serial, `${remote}/${DATABASE}`, env, true);
  const localDb = path.join(work, DATABASE);
  if (dbCat.status !== 0 || dbCat.stdout.length === 0) {
    throw new Error(
      `No history database in the App on ${serial}; start the game once.`
    );
  }
  fs.writeFileSync(localDb, dbCat.stdout);

  const hasJournal =
    appShell(adb, serial, `test -f ${remote}/${DATABASE}-journal`, env, true)
      .status === 0;
  if (hasJournal) {
    const journalCat = appCat(
      adb,
      serial,
      `${remote}/${DATABASE}-journal`,
      env
    );
    fs.writeFileSync(path.join(work, `${DATABASE}-journal`), journalCat.stdout);
  }

  const changesFile = path.join(work, 'changes.txt');
  runSampler(
    [
      command,
      '--data-root',
      work,
      '--journal',
      'delete',
      '--changes',
      changesFile,
      ...extraArgs
    ],
    env
  );
  if (fs.existsSync(path.join(work, `${DATABASE}-journal`))) {
    throw new Error(
      `SQLite left a journal in ${work}; the device was not changed.`
    );
  }

  appPut(adb, serial, localDb, `${remote}/${DATABASE}`, env);
  appShell(
    adb,
    serial,
    `rm -f ${remote}/${DATABASE}-journal ${remote}/${DATABASE}-wal ${remote}/${DATABASE}-shm`,
    env
  );

  if (fs.existsSync(changesFile)) {
    for (const line of fs.readFileSync(changesFile, 'utf8').split(/\r?\n/)) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      const space = trimmed.indexOf(' ');
      if (space === -1) continue;
      const action = trimmed.slice(0, space);
      const relPath = trimmed.slice(space + 1).trim();
      if (action === 'put') {
        appPut(
          adb,
          serial,
          path.join(work, relPath),
          `${remote}/${relPath}`,
          env
        );
      } else if (action === 'rm') {
        appShell(adb, serial, `rm -f ${remote}/${relPath}`, env);
      }
    }
  }
  ok(
    `Updated the App's history on ${serial}; start the game with: just mod::android-launch`
  );
}

export function cliMain(
  argv: readonly string[],
  env: NodeJS.ProcessEnv = process.env
): number {
  const [target, ...rest] = argv;
  if (target === '--help' || target === '-h') {
    console.log(USAGE);
    return 0;
  }
  if (target !== 'desktop' && target !== 'android') {
    console.log(USAGE);
    return 2;
  }
  let command = 'import';
  let extra = rest;
  if (rest[0] === 'import' || rest[0] === 'clear') {
    command = rest[0];
    extra = rest.slice(1);
  }
  try {
    if (target === 'desktop') sampleDesktop(command, extra, env);
    else sampleAndroid(command, extra, env);
    return 0;
  } catch (error) {
    err(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

if (import.meta.main) {
  process.exitCode = cliMain(process.argv.slice(2));
}
