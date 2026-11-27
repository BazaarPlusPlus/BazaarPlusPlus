/**
 * Development only: opens Main Overlay Panels in the running desktop game and
 * saves a capture of each, taken inside the game (no screen-recording
 * permission). Entry point: `just mod::ui-shots`.
 */
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { setTimeout as delay } from 'node:timers/promises';
import {
  desktopDataRoot,
  err,
  gameRoot
} from '../../bazaarplusplus-mod/scripts/mod-env.ts';

const USAGE = `Usage: just mod::ui-shots [panel id ...] [--out DIR] [--prefix NAME]

  panel id   kit-gallery (default), HistoryPanel, LiveBuildPanel, CollectionPanel
  --out      also copy the captures here
  --prefix   name the captures <prefix>-<panel id>.png (default: the panel id alone)

The game must be running with the mod (open "steam://run/1617400"), outside combat. Captures land
in <Data Root>/ui-shots/. BPP_GAME_ROOT overrides the discovered game.`;

export interface DesktopShotsArgs {
  help: boolean;
  panels: string[];
  out: string;
  prefix: string;
}

export function parseDesktopShotsArgs(
  argv: readonly string[]
): DesktopShotsArgs {
  const panels: string[] = [];
  let out = '';
  let prefix = '';
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '-h' || arg === '--help') {
      return { help: true, panels: [], out: '', prefix: '' };
    }
    if (arg === '--out') {
      out = argv[++i] ?? '';
      if (!out) throw new Error('--out requires a directory');
      continue;
    }
    if (arg === '--prefix') {
      prefix = argv[++i] ?? '';
      if (!prefix) throw new Error('--prefix requires a name');
      continue;
    }
    panels.push(arg);
  }
  if (panels.length === 0) panels.push('kit-gallery');
  return { help: false, panels, out, prefix };
}

async function sendCommand(
  commandFile: string,
  command: string,
  timeoutSeconds: number
): Promise<void> {
  fs.writeFileSync(commandFile, `${command}\n`, 'utf8');
  const deadline = Date.now() + timeoutSeconds * 1000;
  while (fs.existsSync(commandFile)) {
    if (Date.now() >= deadline) {
      throw new Error(
        `The game did not take \`${command}\`; is it running with the mod?`
      );
    }
    await delay(250);
  }
}

function matchesLineTokens(text: string, tokens: readonly string[]): boolean {
  for (const line of text.split(/\r?\n/)) {
    let cursor = 0;
    let matched = true;
    for (const token of tokens) {
      const index = line.indexOf(token, cursor);
      if (index === -1) {
        matched = false;
        break;
      }
      cursor = index + token.length;
    }
    if (matched) return true;
  }
  return false;
}

async function awaitLog(
  logFile: string,
  offset: number,
  tokens: readonly string[],
  label: string,
  timeoutSeconds = 15
): Promise<void> {
  const deadline = Date.now() + timeoutSeconds * 1000;
  while (Date.now() < deadline) {
    const size = fs.statSync(logFile).size;
    if (size > offset) {
      const fd = fs.openSync(logFile, 'r');
      try {
        const buffer = Buffer.alloc(size - offset);
        fs.readSync(fd, buffer, 0, buffer.length, offset);
        if (matchesLineTokens(buffer.toString('utf8'), tokens)) return;
      } finally {
        fs.closeSync(fd);
      }
    }
    await delay(250);
  }
  throw new Error(`Timed out waiting for: ${label}`);
}

export async function cliMain(argv: readonly string[]): Promise<number> {
  try {
    const parsed = parseDesktopShotsArgs(argv);
    if (parsed.help) {
      console.log(USAGE);
      return 0;
    }
    const root = gameRoot();
    if (!root) throw new Error('No game found; set BPP_GAME_ROOT.');
    const data = desktopDataRoot(root);
    const logFile = path.join(root, 'BepInEx', 'LogOutput.log');
    const commandFile = path.join(data, 'walkthrough-command.txt');
    if (!fs.statSync(data, { throwIfNoEntry: false })?.isDirectory()) {
      throw new Error(
        `No Data Root at ${data}; start the game with the mod once.`
      );
    }
    if (!fs.statSync(logFile, { throwIfNoEntry: false })?.isFile()) {
      throw new Error(
        `No BepInEx log at ${logFile}; is the game running with the mod?`
      );
    }
    if (parsed.out) fs.mkdirSync(parsed.out, { recursive: true });
    for (const panel of parsed.panels) {
      const lower = panel.toLowerCase();
      const name = parsed.prefix ? `${parsed.prefix}-${lower}` : lower;
      let offset = fs.statSync(logFile).size;
      await sendCommand(commandFile, `open ${panel}`, 10);
      await awaitLog(
        logFile,
        offset,
        [
          'overlay_panels.walkthrough.command',
          `panel_id=${panel}`,
          `open_panel_id=${panel}`
        ],
        `overlay_panels.walkthrough.command.*panel_id=${panel}.*open_panel_id=${panel}`
      );
      // Native art and portraits arrive over a few frames after the open.
      await delay(2000);
      offset = fs.statSync(logFile).size;
      await sendCommand(commandFile, `shot ${name}`, 10);
      await awaitLog(
        logFile,
        offset,
        ['overlay_panels.walkthrough.shot', `name=${name}`],
        `overlay_panels.walkthrough.shot.*name=${name}`
      );
      const shot = path.join(data, 'ui-shots', `${name}.png`);
      if (parsed.out) {
        fs.copyFileSync(shot, path.join(parsed.out, path.basename(shot)));
      }
      console.log(shot);
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
