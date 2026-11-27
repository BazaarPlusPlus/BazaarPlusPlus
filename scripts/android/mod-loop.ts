/**
 * The Android host's device loop (`just mod::android-*`).
 *
 * Drives one device or emulator with the official game and the debug Android
 * App, launching the game from the App's launcher as a player does (ADR 0012):
 * builds, deploys the build's files to their MelonLoader Payload paths,
 * switches patch mode or settings, relaunches, captures evidence, and runs the
 * three-level smoke over all of it. Never installs, uninstalls, or clears data.
 */
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { DatabaseSync } from 'node:sqlite';
import { setTimeout as delay } from 'node:timers/promises';
import {
  collectProps,
  dotnetEnv,
  err,
  ok,
  runDotnet
} from '../../bazaarplusplus-mod/scripts/mod-env.ts';
import { withBuildLock } from '../mod-validation.ts';
import {
  ANDROID_DEV_APP,
  ANDROID_GAME_FILES,
  ANDROID_LAUNCHER_ACTIVITY,
  ANDROID_MELON_BASE,
  adbExec,
  adbExecBuffer,
  appCat,
  appPut,
  appShell,
  requireDevice,
  resolveAdbBinary
} from './adb.ts';
import { inspectScreen } from './android-screen.ts';

const ANDROID_PROJECT =
  'src/BazaarPlusPlus.Android/BazaarPlusPlus.Android.csproj';
const ANDROID_CONFIGURATION = 'Release';
const ANDROID_OUTPUT = `src/BazaarPlusPlus.Android/bin/${ANDROID_CONFIGURATION}/net10.0`;
const ANDROID_DLL = `${ANDROID_OUTPUT}/BazaarPlusPlus.Android.dll`;
const PLAY_LABELS = new Set(['Play', '开始游戏', '開始遊戲']);
const LAUNCHER_TIMEOUT_SECONDS = 90;
const SETTINGS_FILE = `${ANDROID_MELON_BASE}/UserData/BazaarPlusPlus.cfg`;
const DEVICE_LOG = `${ANDROID_MELON_BASE}/MelonLoader/Latest.log`;

export type SmokeWaitState = 'waiting' | 'menu' | 'ready';

export interface ModLoopContext {
  workspaceRoot: string;
  modRoot: string;
  env: NodeJS.ProcessEnv;
}

function defaultContext(): ModLoopContext {
  const workspaceRoot = path.resolve(import.meta.dirname, '../..');
  return {
    workspaceRoot,
    modRoot: path.join(workspaceRoot, 'bazaarplusplus-mod'),
    env: process.env
  };
}

export function updateIniSetting(
  current: string,
  section: string,
  key: string,
  value: string
): string {
  const lines =
    current.length === 0 ? [] : current.replace(/\r\n/g, '\n').split('\n');
  if (lines.length > 0 && lines.at(-1) === '') lines.pop();
  const out: string[] = [];
  let inSection = false;
  let done = false;
  const sectionPattern = new RegExp(`^\\s*\\[${section}\\]\\s*$`);
  const keyPattern = new RegExp(`^\\s*${key}\\s*=`);

  for (const line of lines) {
    if (/^\s*\[/.test(line)) {
      if (inSection && !done) {
        out.push(`${key} = ${value}`);
        out.push('');
        done = true;
      }
      inSection = sectionPattern.test(line);
      out.push(line);
      continue;
    }
    if (inSection && keyPattern.test(line)) {
      out.push(`${key} = ${value}`);
      done = true;
      continue;
    }
    out.push(line);
  }
  if (!done) {
    if (!inSection) {
      out.push('');
      out.push(`[${section}]`);
      out.push('');
    }
    out.push(`${key} = ${value}`);
  }
  return `${out.join('\n')}\n`;
}

export function smokeWaitState(
  needLabel: boolean,
  logText: string
): SmokeWaitState {
  let menu = false;
  let summary = false;
  for (const line of logText.split(/\r?\n/)) {
    if (/event=android\.scene\.loaded .*name=MainMenuScene/.test(line)) {
      menu = true;
    }
    if (menu && /event=android\.hooks\.summary/.test(line)) {
      summary = true;
      if (!needLabel || /version_label_hits=[1-9]/.test(line)) {
        return 'ready';
      }
    }
  }
  return summary ? 'menu' : 'waiting';
}

interface PayloadItem {
  FullPath: string;
  PayloadPath: string;
  ProductAssembly?: string;
}

function collectPayloadFiles(
  ctx: ModLoopContext
): { source: string; payloadPath: string }[] {
  const raw = execFileSync(
    'dotnet',
    [
      'msbuild',
      ANDROID_PROJECT,
      '-nologo',
      `-p:Configuration=${ANDROID_CONFIGURATION}`,
      '-t:CollectPayloadFiles',
      '-getItem:BppPayloadFile'
    ],
    {
      cwd: ctx.modRoot,
      env: dotnetEnv(ctx.env),
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe']
    }
  );
  const parsed = JSON.parse(raw) as {
    Items?: { BppPayloadFile?: PayloadItem[] };
  };
  const items = parsed.Items?.BppPayloadFile ?? [];
  const results: { source: string; payloadPath: string }[] = [];
  for (const item of items) {
    if (item.ProductAssembly === 'true' || fs.existsSync(item.FullPath)) {
      results.push({ source: item.FullPath, payloadPath: item.PayloadPath });
    }
  }
  return results;
}

function cmdBuild(ctx: ModLoopContext, args: readonly string[]): void {
  const props = collectProps(args);
  const code = runDotnet(
    ['build', ANDROID_PROJECT, '-c', ANDROID_CONFIGURATION, ...props],
    { cwd: ctx.modRoot, env: ctx.env }
  );
  if (code !== 0) throw new Error(`dotnet build failed with exit code ${code}`);
}

function cmdPut(ctx: ModLoopContext, args: readonly string[]): void {
  const [hostFile, target] = args;
  if (
    args.length !== 2 ||
    !hostFile ||
    !target ||
    !fs.statSync(hostFile, { throwIfNoEntry: false })?.isFile()
  ) {
    throw new Error(
      "Usage: mod-loop.ts put <file> <path under the game's files>"
    );
  }
  const adb = resolveAdbBinary(ctx.env);
  const serial = requireDevice(adb, ctx.env.ANDROID_SERIAL, ctx.env);
  appPut(adb, serial, hostFile, `${ANDROID_GAME_FILES}/${target}`, ctx.env);
}

function cmdDeploy(ctx: ModLoopContext, args: readonly string[]): void {
  if (args.length !== 0) throw new Error('Usage: just mod::android-deploy');
  const dllPath = path.join(ctx.modRoot, ANDROID_DLL);
  if (!fs.statSync(dllPath, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(
      `No build at ${ANDROID_DLL}; run 'just mod::android-build' first.`
    );
  }
  const adb = resolveAdbBinary(ctx.env);
  const serial = requireDevice(adb, ctx.env.ANDROID_SERIAL, ctx.env);
  const files = collectPayloadFiles(ctx);
  appShell(
    adb,
    serial,
    `rm -f ${ANDROID_MELON_BASE}/UserLibs/BazaarPlusPlus.*.dll`,
    ctx.env
  );
  for (const { source, payloadPath } of files) {
    if (!fs.statSync(source, { throwIfNoEntry: false })?.isFile()) {
      throw new Error(
        `Payload file ${source} is missing; run 'just mod::android-build' first.`
      );
    }
    // The App's runtime owns the Updater (ADR 0012).
    if (payloadPath.startsWith('MelonLoader/Plugins/')) continue;
    appPut(
      adb,
      serial,
      source,
      `${ANDROID_GAME_FILES}/${payloadPath}`,
      ctx.env
    );
  }
  ok(
    `Deployed the MelonLoader Payload to ${serial}; it loads on the next launch.`
  );
}

function writeSetting(
  ctx: ModLoopContext,
  section: string,
  key: string,
  value: string
): void {
  const adb = resolveAdbBinary(ctx.env);
  const serial = requireDevice(adb, ctx.env.ANDROID_SERIAL, ctx.env);
  const existing = appCat(adb, serial, SETTINGS_FILE, ctx.env, true);
  const current = existing.status === 0 ? existing.stdout.toString('utf8') : '';
  const next = updateIniSetting(current, section, key, value);
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-android-cfg-'));
  try {
    const file = path.join(work, 'next.cfg');
    fs.writeFileSync(file, next, 'utf8');
    appPut(adb, serial, file, SETTINGS_FILE, ctx.env);
  } finally {
    fs.rmSync(work, { recursive: true, force: true });
  }
  ok(`[${section}] ${key} = ${value} on ${serial}; relaunch to apply.`);
}

function cmdMode(ctx: ModLoopContext, args: readonly string[]): void {
  const mapping: Record<string, string> = {
    enabled: 'Enabled',
    observe: 'Observe',
    disabled: 'Disabled'
  };
  const mode = args.length === 1 ? mapping[args[0]] : undefined;
  if (!mode) {
    throw new Error('Usage: just mod::android-mode <enabled|observe|disabled>');
  }
  writeSetting(ctx, 'Android', 'PatchMode', mode);
}

function cmdSetting(ctx: ModLoopContext, args: readonly string[]): void {
  const [section, key, value] = args;
  if (
    args.length !== 3 ||
    !/^[A-Za-z0-9_]+$/.test(section) ||
    !/^[A-Za-z0-9_]+$/.test(key) ||
    !/^[A-Za-z0-9_.-]+$/.test(value)
  ) {
    throw new Error(
      'Usage: just mod::android-setting <section> <key> <value> (letters, digits, _ . -)'
    );
  }
  writeSetting(ctx, section, key, value);
}

function openLauncher(
  adb: string,
  serial: string,
  env: NodeJS.ProcessEnv
): void {
  adbExec(adb, serial, ['shell', 'am', 'force-stop', ANDROID_DEV_APP], { env });
  adbExec(adb, serial, ['logcat', '-c'], { env });
  appShell(adb, serial, `rm -f ${DEVICE_LOG}`, env);
  adbExec(
    adb,
    serial,
    ['shell', 'am', 'start', '-n', ANDROID_LAUNCHER_ACTIVITY],
    {
      env
    }
  );
}

async function tapPlay(
  adb: string,
  serial: string,
  env: NodeJS.ProcessEnv
): Promise<void> {
  const deadline = Date.now() + LAUNCHER_TIMEOUT_SECONDS * 1000;
  let lastText = '';
  while (Date.now() < deadline) {
    adbExec(
      adb,
      serial,
      ['shell', 'uiautomator', 'dump', '/data/local/tmp/bpp-ui.xml'],
      { env, allowFailure: true }
    );
    const dumped = adbExec(
      adb,
      serial,
      ['exec-out', 'cat', '/data/local/tmp/bpp-ui.xml'],
      { env, allowFailure: true }
    );
    const xml = dumped.status === 0 ? dumped.stdout : '';
    const node = xml
      .split('>')
      .find((part) =>
        part.includes(`resource-id="${ANDROID_DEV_APP}:id/primary"`)
      );
    const text = node ? (/\btext="([^"]*)"/.exec(node)?.[1] ?? '') : '';
    lastText = text;
    if (text && PLAY_LABELS.has(text) && node) {
      const bounds = /\bbounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"/.exec(node);
      if (bounds) {
        const cx = Math.floor((Number(bounds[1]) + Number(bounds[3])) / 2);
        const cy = Math.floor((Number(bounds[2]) + Number(bounds[4])) / 2);
        adbExec(
          adb,
          serial,
          ['shell', 'input', 'tap', String(cx), String(cy)],
          {
            env
          }
        );
        ok(`Tapped Play on the launcher of ${serial}.`);
        return;
      }
    }
    await delay(2000);
  }
  throw new Error(
    `The launcher on ${serial} did not offer Play within ${LAUNCHER_TIMEOUT_SECONDS}s (it shows "${lastText || 'no primary button'}"); look at its screen.`
  );
}

async function cmdLaunch(
  ctx: ModLoopContext,
  args: readonly string[]
): Promise<void> {
  if (args.length !== 0) throw new Error('Usage: just mod::android-launch');
  const adb = resolveAdbBinary(ctx.env);
  const serial = requireDevice(adb, ctx.env.ANDROID_SERIAL, ctx.env);
  openLauncher(adb, serial, ctx.env);
  await tapPlay(adb, serial, ctx.env);
}

function cmdLauncher(ctx: ModLoopContext, args: readonly string[]): void {
  if (args.length !== 0) throw new Error('Usage: just mod::android-launcher');
  const adb = resolveAdbBinary(ctx.env);
  const serial = requireDevice(adb, ctx.env.ANDROID_SERIAL, ctx.env);
  openLauncher(adb, serial, ctx.env);
}

export function captureRun(
  ctx: ModLoopContext,
  runName: string,
  checkScreen = false
): { outDir: string; problems: string[] } {
  if (!/^[A-Za-z0-9_-]+$/.test(runName)) {
    throw new Error(
      'Usage: just mod::android-capture <run> (letters, digits, - and _)'
    );
  }
  const adb = resolveAdbBinary(ctx.env);
  const serial = requireDevice(adb, ctx.env.ANDROID_SERIAL, ctx.env);
  const outDir = path.join(ctx.modRoot, 'artifacts/android', runName);
  fs.mkdirSync(outDir, { recursive: true });

  fs.writeFileSync(
    path.join(outDir, 'logcat.txt'),
    adbExec(adb, serial, ['logcat', '-d', '-v', 'threadtime'], {
      env: ctx.env
    }).stdout
  );
  fs.writeFileSync(
    path.join(outDir, 'screen.png'),
    adbExecBuffer(adb, serial, ['exec-out', 'screencap', '-p'], {
      env: ctx.env
    }).stdout
  );
  const latest = appCat(adb, serial, DEVICE_LOG, ctx.env, true);
  if (latest.status === 0) {
    fs.writeFileSync(path.join(outDir, 'Latest.log'), latest.stdout);
  } else {
    err(`No ${DEVICE_LOG} on ${serial}.`);
  }
  fs.writeFileSync(
    path.join(outDir, 'wm-size.txt'),
    adbExec(adb, serial, ['shell', 'wm', 'size'], { env: ctx.env }).stdout
  );
  fs.writeFileSync(
    path.join(outDir, 'wm-density.txt'),
    adbExec(adb, serial, ['shell', 'wm', 'density'], { env: ctx.env }).stdout
  );
  fs.writeFileSync(
    path.join(outDir, 'user-rotation.txt'),
    adbExec(
      adb,
      serial,
      ['shell', 'settings', 'get', 'system', 'user_rotation'],
      { env: ctx.env }
    ).stdout
  );
  fs.writeFileSync(
    path.join(outDir, 'accelerometer-rotation.txt'),
    adbExec(
      adb,
      serial,
      ['shell', 'settings', 'get', 'system', 'accelerometer_rotation'],
      { env: ctx.env }
    ).stdout
  );
  fs.writeFileSync(
    path.join(outDir, 'display.txt'),
    adbExec(adb, serial, ['shell', 'dumpsys', 'display'], { env: ctx.env })
      .stdout
  );

  const report = inspectScreen(outDir, serial);
  fs.writeFileSync(
    path.join(outDir, 'environment.json'),
    `${JSON.stringify(report, null, 2)}\n`
  );
  ok(`Captured ${runName} into ${outDir}`);
  if (checkScreen && report.problems.length > 0) {
    throw new Error(report.problems.join('\n'));
  }
  return { outDir, problems: report.problems };
}

function pullDatabase(
  adb: string,
  serial: string,
  outDir: string,
  env: NodeJS.ProcessEnv
): void {
  const logFile = path.join(outDir, 'Latest.log');
  const dbText = path.join(outDir, 'database.txt');
  if (!fs.existsSync(logFile)) {
    fs.writeFileSync(dbText, 'No Latest.log; no database path to pull.\n');
    return;
  }
  const log = fs.readFileSync(logFile, 'utf8');
  const match =
    /event=android\.record_chain\.started .*database_path=([^\s]+)/.exec(log);
  if (!match) {
    fs.writeFileSync(
      dbText,
      'The record chain did not start; no database to pull.\n'
    );
    return;
  }
  const remotePath = match[1];
  const pulled = appCat(adb, serial, remotePath, env, true);
  if (pulled.status !== 0) {
    fs.writeFileSync(dbText, `run-as cat ${remotePath} failed.\n`);
    return;
  }
  const dbFile = path.join(outDir, 'bazaarplusplus.db');
  fs.writeFileSync(dbFile, pulled.stdout);
  const db = new DatabaseSync(dbFile, { readOnly: true });
  try {
    const versionRow = db.prepare('PRAGMA user_version').get() as {
      user_version: number;
    };
    const tables = db
      .prepare(
        "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
      )
      .all() as { name: string }[];
    const lines = [remotePath, `user_version ${versionRow.user_version}`];
    for (const { name } of tables) {
      const escaped = name.replaceAll('"', '""');
      const row = db
        .prepare(`SELECT count(*) AS count FROM "${escaped}"`)
        .get() as { count: number };
      lines.push(`${name} ${row.count}`);
    }
    fs.writeFileSync(dbText, `${lines.join('\n')}\n`);
  } finally {
    db.close();
  }
}

export function verifySmokeDir(ctx: ModLoopContext, dir: string): void {
  if (!dir || !fs.statSync(dir, { throwIfNoEntry: false })?.isDirectory()) {
    throw new Error('Usage: just mod::android-verify <capture dir>');
  }
  const versionFile = path.join(dir, 'version.txt');
  const version = (
    fs.existsSync(versionFile)
      ? fs.readFileSync(versionFile, 'utf8')
      : fs.readFileSync(path.join(ctx.workspaceRoot, 'VERSION'), 'utf8')
  ).trim();
  const dllFile = path.join(dir, 'dll.sha256');
  const built = fs.existsSync(dllFile)
    ? fs.readFileSync(dllFile, 'utf8').trim()
    : '';
  const logFile = path.join(dir, 'Latest.log');
  const logLines = fs.existsSync(logFile)
    ? fs.readFileSync(logFile, 'utf8').split(/\r?\n/)
    : [];

  const loaded =
    logLines.find((line) => line.includes('event=android.plugin.loaded ')) ??
    '';
  const sha = / sha256=([0-9a-f]+)/.exec(loaded)?.[1] ?? '';
  const installed = logLines.filter((line) =>
    line.includes(
      'event=android.patch.installed patch_type=BazaarPlusPlus.Android.Patches.VersionLabelPatch '
    )
  ).length;
  const hitValues = logLines
    .map(
      (line) =>
        /event=android\.hooks\.summary .*version_label_hits=(\d+)/.exec(
          line
        )?.[1]
    )
    .filter((value): value is string => value !== undefined);
  const firstHits = hitValues[0];
  const lastHits = hitValues.at(-1);
  let label = '';
  for (const line of logLines) {
    if (!/event=android\.version_label\.hit .*write=true/.test(line)) continue;
    const after = / after="([^"]*)"/.exec(line)?.[1];
    if (after && after.includes(`| BPP ${version} `)) {
      label = after;
      break;
    }
  }

  const injection = built.length === 64 && sha === built ? 'pass' : 'fail';
  const hooks = installed > 0 && Number(lastHits ?? 0) > 0 ? 'pass' : 'fail';
  const feature = label.length > 0 ? 'pass' : 'fail';
  const labelCell = label.replaceAll('|', '\\|');

  const lines: string[] = [
    `# Android smoke: ${path.basename(dir)}`,
    '',
    '| Level | Result | Evidence |',
    '|---|---|---|',
    `| Injection | ${injection} | plugin.loaded sha256 \`${sha || 'none'}\`, built \`${built || 'none'}\` |`,
    `| Hook hit | ${hooks} | VersionLabelPatch installed ${installed} time(s); version_label_hits ${firstHits ?? 'none'} in the first summary, ${lastHits ?? 'none'} in the last |`,
    `| Feature | ${feature} | version label \`${labelCell || `not written with BPP ${version}`}\` |`,
    '',
    `Loaded: \`${loaded || 'no android.plugin.loaded line'}\``
  ];
  const dbText = path.join(dir, 'database.txt');
  if (fs.existsSync(dbText)) {
    lines.push('', 'Database (evidence only):', '');
    for (const dbLine of fs.readFileSync(dbText, 'utf8').split(/\r?\n/)) {
      lines.push(`    ${dbLine}`);
    }
  }
  const reportPath = path.join(dir, 'smoke.md');
  fs.writeFileSync(reportPath, `${lines.join('\n')}\n`);

  for (const [level, status] of [
    ['Injection', injection],
    ['Hook hit', hooks],
    ['Feature', feature]
  ]) {
    if (status !== 'pass') {
      throw new Error(`Smoke failed at "${level}"; see ${reportPath}`);
    }
  }
  ok(`Smoke passed all three levels; see ${reportPath}`);
}

async function cmdSmoke(
  ctx: ModLoopContext,
  args: readonly string[]
): Promise<void> {
  const mode = args[0];
  if (args.length !== 1 || !['enabled', 'observe', 'disabled'].includes(mode)) {
    throw new Error(
      'Usage: just mod::android-smoke <enabled|observe|disabled>'
    );
  }
  const adb = resolveAdbBinary(ctx.env);
  const serial = requireDevice(adb, ctx.env.ANDROID_SERIAL, ctx.env);
  const runName = `smoke-${mode}`;
  const outDir = path.join(ctx.modRoot, 'artifacts/android', runName);
  fs.rmSync(outDir, { recursive: true, force: true });

  cmdBuild(ctx, []);
  cmdDeploy(ctx, []);
  cmdMode(ctx, [mode]);
  await cmdLaunch(ctx, []);

  const timeoutSec = Number(ctx.env.BPP_ANDROID_SMOKE_TIMEOUT || 300);
  const needLabel = mode !== 'disabled';
  const deadline = Date.now() + timeoutSec * 1000;
  let state: SmokeWaitState = 'waiting';
  while (Date.now() < deadline) {
    const cat = appCat(adb, serial, DEVICE_LOG, ctx.env, true);
    const log = cat.status === 0 ? cat.stdout.toString('utf8') : '';
    state = smokeWaitState(needLabel, log);
    if (state === 'ready') {
      ok('Main menu reached; a hooks summary followed it.');
      break;
    }
    await delay(5000);
  }
  let menuReached = true;
  if (state === 'menu') {
    err(
      `Main menu reached, but no hooks summary counted a version label hit within ${timeoutSec}s; capturing anyway.`
    );
  } else if (state !== 'ready') {
    err(
      `No main menu and following hooks summary within ${timeoutSec}s; capturing anyway.`
    );
    menuReached = false;
  }

  captureRun(ctx, runName, false);
  const dllItem = collectPayloadFiles(ctx).find(
    (item) => item.payloadPath === 'MelonLoader/Mods/BazaarPlusPlus.Android.dll'
  );
  if (!dllItem || !fs.existsSync(dllItem.source)) {
    throw new Error(
      'No built BazaarPlusPlus.Android.dll in the Payload files.'
    );
  }
  const sha = crypto
    .createHash('sha256')
    .update(fs.readFileSync(dllItem.source))
    .digest('hex');
  fs.writeFileSync(path.join(outDir, 'dll.sha256'), `${sha}\n`);
  fs.writeFileSync(
    path.join(outDir, 'version.txt'),
    `${fs.readFileSync(path.join(ctx.workspaceRoot, 'VERSION'), 'utf8').trim()}\n`
  );
  pullDatabase(adb, serial, outDir, ctx.env);
  verifySmokeDir(ctx, outDir);
  if (!menuReached) {
    throw new Error(
      `The main menu was not reached in ${timeoutSec}s; see ${path.join(outDir, 'screen.png')}`
    );
  }
}

const LOCKED_COMMANDS = new Set(['build', 'deploy', 'smoke']);

export async function cliMain(
  argv: readonly string[],
  ctxInput: Partial<ModLoopContext> = {}
): Promise<number> {
  const base = defaultContext();
  const ctx: ModLoopContext = {
    workspaceRoot: ctxInput.workspaceRoot ?? base.workspaceRoot,
    modRoot:
      ctxInput.modRoot ??
      path.join(
        ctxInput.workspaceRoot ?? base.workspaceRoot,
        'bazaarplusplus-mod'
      ),
    env: ctxInput.env ?? base.env
  };
  const [command, ...rest] = argv;
  const runCommand = async (env: NodeJS.ProcessEnv): Promise<void> => {
    const active: ModLoopContext = { ...ctx, env };
    switch (command) {
      case 'build':
        cmdBuild(active, rest);
        return;
      case 'deploy':
        cmdDeploy(active, rest);
        return;
      case 'put':
        cmdPut(active, rest);
        return;
      case 'mode':
        cmdMode(active, rest);
        return;
      case 'setting':
        cmdSetting(active, rest);
        return;
      case 'launch':
        await cmdLaunch(active, rest);
        return;
      case 'launcher':
        cmdLauncher(active, rest);
        return;
      case 'capture':
        if (rest.length !== 1)
          throw new Error(
            'Usage: just mod::android-capture <run> (letters, digits, - and _)'
          );
        captureRun(active, rest[0], false);
        return;
      case 'screen-check':
        if (rest.length !== 1)
          throw new Error(
            'Usage: just mod::android-screen-check <run> (letters, digits, - and _)'
          );
        captureRun(active, rest[0], true);
        return;
      case 'smoke':
        await cmdSmoke(active, rest);
        return;
      case 'verify':
        if (rest.length !== 1)
          throw new Error('Usage: just mod::android-verify <capture dir>');
        verifySmokeDir(active, path.resolve(ctx.modRoot, rest[0]));
        return;
      default:
        throw new Error(
          "Usage: mod-loop.ts <build|deploy|put|mode|setting|launch|launcher|capture|screen-check|smoke|verify> [args...]; see 'just --list mod'."
        );
    }
  };

  try {
    if (command && LOCKED_COMMANDS.has(command)) {
      await withBuildLock(ctx.workspaceRoot, (lockedEnv) =>
        runCommand({
          ...ctx.env,
          BPP_MOD_BUILD_LOCK: lockedEnv.BPP_MOD_BUILD_LOCK
        })
      );
    } else {
      await runCommand(ctx.env);
    }
    return 0;
  } catch (error) {
    err(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

if (import.meta.main) {
  process.exitCode = await cliMain(process.argv.slice(2));
}
