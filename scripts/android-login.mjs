import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import {
  assertExternal,
  configHome,
  readConfig,
  resolveValues
} from './local-config.mjs';

const app = 'com.bazaarplusplus.android.dev';
const prefs = 'shared_prefs/com.tempogames.thebazaar.v2.playerprefs.xml';
const activity = `${app}/com.bazaarplusplus.android.launch.LauncherActivity`;

const help = `Usage: just android::dev-login <serial>

Read config.ini [game] BPP_GAME_EMAIL and BPP_GAME_PASSWORD, stop the debug
Android App, update only the game's saved email/password and open the launcher.
Choose the BPP launch action there; the game uses its normal automatic login.
An exported BPP_GAME_EMAIL or BPP_GAME_PASSWORD overrides the corresponding key.

Requires adb, Python 3 and an installed debug App. The device serial is mandatory.
Credentials travel through stdin, never command arguments or temporary files on
the host. The game's PlayerPrefs retain them in the debug App's private sandbox.
The command does not install an APK, reset game data or change the official App.
`;

// ElementTree preserves unrelated preference values and types. Avoid a second
// INI parser: the shared local-config module owns quoting and validation.
const patchXml = String.raw`
import json, sys, xml.etree.ElementTree as ET
try:
    data = json.load(sys.stdin)
    text = data['xml']
    if '<!DOCTYPE' in text or '<!ENTITY' in text:
        raise ValueError()
    root = ET.fromstring(text)
    if root.tag != 'map':
        raise ValueError()
    for key in ('email', 'password'):
        matches = [child for child in root if child.get('name') == key]
        if len(matches) > 1 or any(child.tag != 'string' for child in matches):
            raise ValueError()
        node = matches[0] if matches else ET.SubElement(root, 'string', name=key)
        node.text = data[key]
    result = ET.tostring(root, encoding='utf-8', xml_declaration=True)
    ET.fromstring(result)
    sys.stdout.buffer.write(result)
except Exception:
    sys.exit(1)
`;

function execute(command, args, input, env, label) {
  const result = spawnSync(command, args, {
    input,
    env,
    encoding: 'utf8',
    maxBuffer: 4 * 1024 * 1024,
    timeout: 30_000,
    windowsHide: true
  });
  // Child output and spawn errors can contain credentials. Report only the step.
  if (result.error || result.status !== 0)
    throw new Error(`${label} failed; child output withheld.`);
  return result.stdout;
}

export function updatePreferences(xml, email, password, env = process.env) {
  return execute(
    'python3',
    ['-c', patchXml],
    JSON.stringify({ xml, email, password }),
    env,
    'PlayerPrefs XML validation/update (requires Python 3)'
  );
}

export function applyLogin(serial, { env = process.env, run = execute } = {}) {
  if (!serial || !/^(?!-)[A-Za-z0-9_.:\[\]-]+$/.test(serial))
    throw new Error('Provide an explicit adb device serial; see --help.');
  const home = configHome(env);
  assertExternal(home);
  const config = readConfig(home);
  const file = path.join(home, 'config.ini');
  if (process.platform !== 'win32' && fs.existsSync(file))
    fs.chmodSync(file, 0o600);
  const values = resolveValues(config, 'game', env);
  const email = values.BPP_GAME_EMAIL;
  const password = values.BPP_GAME_PASSWORD;
  if (!email?.trim() || !password)
    throw new Error(
      'Set BPP_GAME_EMAIL and BPP_GAME_PASSWORD in config.ini [game].'
    );

  const childEnv = { ...env };
  delete childEnv.BPP_GAME_EMAIL;
  delete childEnv.BPP_GAME_PASSWORD;
  const adb = (args, input, label) =>
    run('adb', ['-s', serial, ...args], input, childEnv, label);
  const shell = (script, input, label) =>
    adb(['shell', '-T', `run-as ${app} sh -c '${script}'`], input, label);

  // Check both prerequisites before stopping the App or touching preferences.
  updatePreferences('<map />', email, password, childEnv);
  adb(['shell', 'run-as', app, 'true'], undefined, 'Debug App access');
  adb(['shell', 'am', 'force-stop', app], undefined, 'Stopping debug App');
  // Android restores .bak before reading the main file after an interrupted write.
  const xml = shell(
    `if [ -f ${prefs}.bak ]; then cat ${prefs}.bak; elif [ -f ${prefs} ]; then cat ${prefs}; else printf "<map />"; fi`,
    undefined,
    'Reading PlayerPrefs'
  );
  const updated = updatePreferences(xml, email, password, childEnv);
  shell(
    `umask 077; trap "rm -f ${prefs}.bpp-tmp" EXIT; mkdir -p shared_prefs && cat > ${prefs}.bpp-tmp && chmod 600 ${prefs}.bpp-tmp && mv ${prefs}.bpp-tmp ${prefs} && rm -f ${prefs}.bak`,
    updated,
    'Writing PlayerPrefs'
  );
  const actual = shell(`cat ${prefs}`, undefined, 'Verifying PlayerPrefs');
  if (actual !== updated)
    throw new Error('PlayerPrefs readback did not match; App remains stopped.');
  adb(['shell', 'am', 'start', '-n', activity], undefined, 'Opening launcher');
}

if (
  process.argv[1] &&
  import.meta.url === pathToFileURL(process.argv[1]).href
) {
  try {
    const args = process.argv.slice(2);
    if (args.length === 1 && ['--help', '-h'].includes(args[0])) {
      console.log(help);
    } else {
      if (args.length !== 1) throw new Error(help);
      applyLogin(args[0]);
      console.log(
        'Saved game credentials verified in the debug App. Launcher opened; choose BPP launch to sign in.'
      );
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
