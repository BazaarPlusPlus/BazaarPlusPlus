import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import type { SpawnSyncOptions } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { profiles as androidProfiles, onlyDevice } from './setup-device.ts';
import type { ScreenReport } from './android-screen.ts';
import { androidSdkPaths } from './android-sdk.ts';

/** A walkthrough device: an Android AVD of the matrix, or the desktop game. */
export type Device =
  | { name: string; kind: 'android' }
  | { name: string; kind: 'desktop' };

// A device a command reached: an Android one carries the running serial.
type ReachedDevice =
  | { name: string; kind: 'android'; serial: string }
  | { name: string; kind: 'desktop' };

// A recorded screenshot cell, as capture records it.
interface CaptureCell {
  device: string;
  scenario: string;
  file: string;
  problems: string[];
  note: string;
  skipped?: undefined;
}

// A cell skip records, with the reason the screen was not reached.
interface SkipCell {
  device: string;
  scenario: string;
  skipped: string;
}

/** One walkthrough.json cell: a screenshot or a skip, with when it was recorded. */
export type WalkthroughCell = (CaptureCell | SkipCell) & { recordedAt: string };

/** walkthrough.json: the run, the checkout it ran from and its cells. */
export interface WalkthroughManifest {
  run: string;
  sha: string;
  dirty: boolean;
  createdAt: string;
  cells: WalkthroughCell[];
}

const root = path.resolve(import.meta.dirname, '../..');
const runs = path.join(root, 'artifacts/walkthrough');
const commandFile = 'walkthrough-command.txt';
const dataRootName = 'BazaarPlusPlusV6';
// The debug Android App's game files, relative to run-as (android.sh owns the layout).
const androidApp = 'com.bazaarplusplus.android.dev';
const androidCommand = `no_backup/game-external/${dataRootName}/${commandFile}`;
const androidScript = path.join(root, 'bazaarplusplus-mod/scripts/android.sh');
const defaultGameRoot = path.join(
  os.homedir(),
  'Library/Application Support/Steam/steamapps/common/The Bazaar'
);
// The registered Overlay Panel Host ids: the Mobile UI's on Android, the desktop panels' own.
const panels: Record<'mobile' | 'desktop', Record<string, string>> = {
  mobile: {
    collection: 'collection',
    history: 'history',
    live_build: 'live_build'
  },
  desktop: {
    collection: 'CollectionPanel',
    history: 'HistoryPanel',
    live_build: 'LiveBuildPanel'
  }
};
// The control polls every half second; a frame-starved loading screen can stretch that.
const consumeTimeoutMs = 15_000;
const id = /^[A-Za-z0-9_-]+$/;

export const help = `Device walkthrough: open panels, capture screens, build the HTML grid

  bun scripts/android/walkthrough.ts devices
  bun scripts/android/walkthrough.ts panel DEVICE collection|history|live_build
  bun scripts/android/walkthrough.ts capture RUN DEVICE SCENARIO [--note TEXT] [--portrait]
  bun scripts/android/walkthrough.ts skip RUN DEVICE SCENARIO --reason TEXT
  bun scripts/android/walkthrough.ts report RUN

DEVICE is an Android AVD name from the device matrix, or
desktop; an Android DEVICE must be the one device running. RUN and
SCENARIO use letters, digits, - and _. panel writes the
Walkthrough Control's command file into the device's Data Root and waits until
the game consumes it; the game log line overlay_panels.walkthrough.command says
which panel is open afterwards. capture saves RUN/DEVICE/SCENARIO.png (Android
through mod::android-capture, which also rejects portrait or overridden
displays; --portrait takes a plain screenshot, for the App's launcher; desktop
rejects a game window that is not 16:9);
recapturing a cell replaces it. report writes index.html, scenarios
in first-recorded order, devices in matrix order.

BPP_GAME_ROOT overrides the desktop game folder (default: Steam's The Bazaar).
Desktop capture needs Screen Recording permission for the terminal.
Evidence: artifacts/walkthrough/RUN/ (private: it shows the signed-in account).
`;

export function devices(): Device[] {
  return [
    ...androidProfiles.map((p): Device => ({ name: p.name, kind: 'android' })),
    { name: 'desktop', kind: 'desktop' }
  ];
}

function device(name: string): Device {
  const found = devices().find((d) => d.name === name);
  if (!found)
    throw new Error(
      `Unknown device ${name}. Devices: ${devices()
        .map((d) => d.name)
        .join(', ')}.`
    );
  return found;
}

// The running device, checked to be the AVD asked for, so no capture is filed under another one.
function online(d: Device & { kind: 'android' }): ReachedDevice {
  const serial = onlyDevice(
    run([adbPath(), 'devices']),
    process.env.ANDROID_SERIAL
  );
  let avd: string;
  try {
    avd = run([adbPath(), '-s', serial, 'emu', 'avd', 'name'])
      .trim()
      .split(/\r?\n/)[0];
  } catch {
    avd = 'not an emulator';
  }
  if (avd !== d.name)
    throw new Error(
      `The running device ${serial} is ${avd}, not ${d.name}. Close it and open ${d.name}: just setup-device ${d.name} --keep-open`
    );
  return { ...d, serial };
}

const reach = (d: Device): ReachedDevice =>
  d.kind === 'android' ? online(d) : d;

function run(
  args: string[],
  options: Omit<SpawnSyncOptions, 'encoding'> = {}
): string {
  const result = spawnSync(args[0], args.slice(1), {
    encoding: 'utf8',
    timeout: 120_000,
    windowsHide: true,
    ...options
  });
  if (result.error || result.status !== 0)
    throw new Error(
      `${args.slice(0, 3).join(' ')} failed: ${result.error?.message || result.stderr?.trim() || `exit ${result.status}`}`
    );
  return result.stdout.replace(/\r+\n/g, '\n');
}

// Keep the serial online() checked even if another device appears later.
const androidScriptRun = (d: { serial: string }, ...args: string[]): string =>
  run(['bash', androidScript, ...args], {
    cwd: path.join(root, 'bazaarplusplus-mod'),
    env: { ...process.env, ADB: adbPath(), ANDROID_SERIAL: d.serial }
  });

const adbPath = (): string => androidSdkPaths().adb;

const adb = (d: { serial: string }, ...args: string[]): string =>
  run([adbPath(), '-s', d.serial, ...args]);

const desktopDataRoot = (): string =>
  path.join(process.env.BPP_GAME_ROOT || defaultGameRoot, dataRootName);

export async function panel(name: string, logical: string): Promise<void> {
  const d = reach(device(name));
  const target = panels[d.kind === 'desktop' ? 'desktop' : 'mobile'][logical];
  if (!target)
    throw new Error(
      `Unknown panel ${logical}. Panels: ${Object.keys(panels.mobile).join(', ')}.`
    );
  const text = `open ${target}\n`;
  let exists: () => boolean, remove: () => void;
  if (d.kind === 'android') {
    const local = path.join(
      fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-walk-')),
      commandFile
    );
    fs.writeFileSync(local, text);
    androidScriptRun(d, 'put', local, `${dataRootName}/${commandFile}`);
    fs.rmSync(path.dirname(local), { recursive: true, force: true });
    exists = () =>
      adb(
        d,
        'shell',
        `run-as ${androidApp} sh -c 'test -e ${androidCommand} && echo yes || true'`
      ).trim() === 'yes';
    remove = () =>
      adb(d, 'shell', 'run-as', androidApp, 'rm', '-f', androidCommand);
  } else {
    const directory = desktopDataRoot();
    if (!fs.existsSync(directory))
      throw new Error(
        `${directory} does not exist: the mod has not run on ${d.name} yet. Launch the game first.`
      );
    const file = path.join(directory, commandFile);
    fs.writeFileSync(file, text);
    exists = () => fs.existsSync(file);
    remove = () => fs.rmSync(file, { force: true });
  }
  const deadline = Date.now() + consumeTimeoutMs;
  while (exists()) {
    if (Date.now() > deadline) {
      // A stale command would open the panel at the next launch.
      remove();
      throw new Error(
        `The game did not consume ${commandFile} on ${d.name} within ${consumeTimeoutMs / 1000}s; the request was withdrawn. Is the game running with this checkout's mod? A file it could not delete stops the control until the next launch (overlay_panels.walkthrough.stopped).`
      );
    }
    await delay(250);
  }
  console.log(
    `${d.name}: requested ${target}; check the screen or the log for the result.`
  );
}

const manifestPath = (runName: string): string =>
  path.join(runs, runName, 'walkthrough.json');

function readManifest(runName: string): WalkthroughManifest {
  const file = manifestPath(runName);
  if (fs.existsSync(file))
    return JSON.parse(fs.readFileSync(file, 'utf8')) as WalkthroughManifest;
  const sha = spawnSync('git', ['rev-parse', '--short', 'HEAD'], {
    cwd: root,
    encoding: 'utf8'
  }).stdout.trim();
  const dirty =
    spawnSync('git', ['status', '--porcelain'], {
      cwd: root,
      encoding: 'utf8'
    }).stdout.trim() !== '';
  return {
    run: runName,
    sha,
    dirty,
    createdAt: new Date().toISOString(),
    cells: []
  };
}

function record(runName: string, cell: CaptureCell | SkipCell): void {
  const manifest = readManifest(runName);
  manifest.cells = manifest.cells.filter(
    (c) => !(c.device === cell.device && c.scenario === cell.scenario)
  );
  manifest.cells.push({ ...cell, recordedAt: new Date().toISOString() });
  fs.mkdirSync(path.dirname(manifestPath(runName)), { recursive: true });
  fs.writeFileSync(
    manifestPath(runName),
    JSON.stringify(manifest, null, 2) + '\n'
  );
}

function checkIds(...values: string[]): void {
  for (const value of values)
    if (!id.test(value || ''))
      throw new Error(
        `${value} is not a RUN or SCENARIO id (letters, digits, - and _).`
      );
}

// The desktop grid is compared at one aspect ratio. Window bounds include a windowed game's
// title bar and a full-screen game has none, so either reading may be the 16:9 picture.
const desktopTitleBar = 28;
export function desktopAspectProblem(
  width: number,
  height: number
): string | null {
  const is16by9 = (h: number): boolean =>
    Math.abs(width / h / (16 / 9) - 1) <= 0.01;
  if (is16by9(height) || is16by9(height - desktopTitleBar)) return null;
  return `The game window is ${width}x${height} points, not 16:9. Quit the game, set its resolution as the walkthrough skill says, and launch it again.`;
}

// The game window's CGWindowID, for screencapture -l: the largest window the game owns, since
// it also owns thin menu-bar strips, and a full-screen game sits in its own Space, off screen.
function desktopWindow(): string {
  const script = `import CoreGraphics
let windows = CGWindowListCopyWindowInfo([.optionAll], kCGNullWindowID) as? [[String: Any]] ?? []
let game = windows.filter { (($0["kCGWindowOwnerName"] as? String) ?? "") == "TheBazaar" && ($0["kCGWindowLayer"] as? Int) == 0 }
func area(_ w: [String: Any]) -> Double { let b = w["kCGWindowBounds"] as? [String: Any] ?? [:]; return ((b["Width"] as? Double) ?? 0) * ((b["Height"] as? Double) ?? 0) }
if let w = game.max(by: { area($0) < area($1) }), let b = w["kCGWindowBounds"] as? [String: Any] { print(w["kCGWindowNumber"] as! Int, b["Width"] as! Double, b["Height"] as! Double) }`;
  const window = run(['xcrun', 'swift', '-e', script]).trim();
  if (!window)
    throw new Error(
      'The Bazaar has no window on screen. Launch it through Steam first.'
    );
  const [id, width, height] = window.split(' ');
  const problem = desktopAspectProblem(Number(width), Number(height));
  if (problem) throw new Error(problem);
  return id;
}

export function capture(
  runName: string,
  name: string,
  scenario: string,
  note: string | undefined,
  portrait = false
): void {
  checkIds(runName, scenario);
  const d = reach(device(name));
  const file = path.join(runs, runName, d.name, `${scenario}.png`);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  let problems: string[] = [];
  if (d.kind === 'android' && portrait) {
    // The launcher is portrait by design; the game's landscape check does not apply.
    fs.writeFileSync(
      file,
      spawnSync(adbPath(), ['-s', d.serial, 'exec-out', 'screencap', '-p'], {
        maxBuffer: 64 * 1024 * 1024
      }).stdout
    );
  } else if (d.kind === 'android') {
    const capture = `walkthrough-${runName}-${d.name}-${scenario}`;
    androidScriptRun(d, 'capture', capture);
    const out = path.join(
      root,
      'bazaarplusplus-mod/artifacts/android',
      capture
    );
    fs.copyFileSync(path.join(out, 'screen.png'), file);
    problems = (
      JSON.parse(
        fs.readFileSync(path.join(out, 'environment.json'), 'utf8')
      ) as ScreenReport
    ).problems;
  } else {
    run(['screencapture', '-x', '-o', `-l${desktopWindow()}`, file]);
  }
  record(runName, {
    device: d.name,
    scenario,
    file: path.relative(path.join(runs, runName), file),
    problems,
    note: note || ''
  });
  console.log(
    `${d.name}/${scenario}: ${file}${problems.length ? `\n  problems: ${problems.join(' ')}` : ''}`
  );
}

export function skip(
  runName: string,
  name: string,
  scenario: string,
  reason: string | undefined
): void {
  checkIds(runName, scenario);
  if (!reason)
    throw new Error(
      'skip needs --reason TEXT: say why the screen was not reached.'
    );
  record(runName, { device: device(name).name, scenario, skipped: reason });
  console.log(`${name}/${scenario}: skipped (${reason})`);
}

const escape = (text: string): string =>
  String(text).replace(
    /[&<>"]/g,
    (c) =>
      (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' } as Record<
          string,
          string
        >
      )[c]
  );

export function renderReport(manifest: WalkthroughManifest): string {
  const order = devices().map((d) => d.name);
  const shown = order.filter((name) =>
    manifest.cells.some((c) => c.device === name)
  );
  const scenarios = [...new Set(manifest.cells.map((c) => c.scenario))];
  const cell = (scenario: string, name: string): string => {
    const c = manifest.cells.find(
      (x) => x.scenario === scenario && x.device === name
    );
    if (!c) return '<td class="missing">未记录</td>';
    if (c.skipped) return `<td class="skipped">跳过：${escape(c.skipped)}</td>`;
    // skip refuses an empty reason, so every other cell is a capture.
    if (!('problems' in c))
      throw new Error(`${name}/${scenario} is neither captured nor skipped.`);
    const notes = [
      ...c.problems.map((p) => `<p class="problem">${escape(p)}</p>`),
      c.note ? `<p class="note">${escape(c.note)}</p>` : ''
    ].join('');
    return `<td><a href="${escape(c.file)}" target="_blank"><img src="${escape(c.file)}" loading="lazy" alt="${escape(`${name} ${scenario}`)}"></a>${notes}</td>`;
  };
  return `<!doctype html>
<html lang="zh-Hans">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>走查 ${escape(manifest.run)}</title>
<style>
:root { color-scheme: light dark; --bg: #fff; --fg: #1d1d1f; --muted: #6e6e73; --line: #d2d2d7; --warn: #b3261e; }
@media (prefers-color-scheme: dark) { :root { --bg: #1c1c1e; --fg: #f5f5f7; --muted: #a1a1a6; --line: #3a3a3c; --warn: #ff6b60; } }
body { margin: 0; padding: 16px; background: var(--bg); color: var(--fg); font: 14px/1.4 -apple-system, system-ui, sans-serif; }
h1 { font-size: 18px; margin: 0 0 4px; }
header p { margin: 0 0 16px; color: var(--muted); }
.grid { overflow: auto; }
table { border-collapse: collapse; }
th, td { border: 1px solid var(--line); padding: 8px; vertical-align: top; text-align: left; }
thead th { position: sticky; top: 0; background: var(--bg); }
tbody th { position: sticky; left: 0; background: var(--bg); white-space: nowrap; }
td { width: 360px; min-width: 240px; }
img { width: 100%; display: block; }
.missing, .skipped { color: var(--muted); }
.problem { color: var(--warn); margin: 6px 0 0; }
.note { margin: 6px 0 0; }
</style>
</head>
<body>
<header>
<h1>走查 ${escape(manifest.run)}</h1>
<p>${escape(manifest.sha)}${manifest.dirty ? '（有未提交改动）' : ''} · ${escape(manifest.createdAt)}</p>
</header>
<div class="grid">
<table>
<thead><tr><th></th>${shown.map((name) => `<th>${escape(name)}</th>`).join('')}</tr></thead>
<tbody>
${scenarios.map((s) => `<tr><th>${escape(s)}</th>${shown.map((name) => cell(s, name)).join('')}</tr>`).join('\n')}
</tbody>
</table>
</div>
</body>
</html>
`;
}

export function report(runName: string): void {
  checkIds(runName);
  if (!fs.existsSync(manifestPath(runName)))
    throw new Error(
      `No walkthrough ${runName}: capture or skip a screen first.`
    );
  const out = path.join(runs, runName, 'index.html');
  fs.writeFileSync(out, renderReport(readManifest(runName)));
  console.log(out);
}

export async function main(args: string[]): Promise<void> {
  const [command, ...rest] = args;
  const option = (flag: string): string | undefined => {
    const at = rest.indexOf(flag);
    return at < 0 ? undefined : rest[at + 1];
  };
  switch (command) {
    case 'devices':
      for (const d of devices()) console.log(`${d.name.padEnd(17)} ${d.kind}`);
      return;
    case 'panel':
      return panel(rest[0], rest[1]);
    case 'capture':
      return capture(
        rest[0],
        rest[1],
        rest[2],
        option('--note'),
        rest.includes('--portrait')
      );
    case 'skip':
      return skip(rest[0], rest[1], rest[2], option('--reason'));
    case 'report':
      return report(rest[0]);
    default:
      console.log(help);
      if (command && command !== '--help') process.exitCode = 1;
  }
}

if (import.meta.main) {
  try {
    await main(process.argv.slice(2));
  } catch (error) {
    console.error((error as Error).message);
    process.exitCode = 1;
  }
}
