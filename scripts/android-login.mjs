import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { pathToFileURL } from 'node:url';
import {
  assertExternal,
  configHome,
  readConfig,
  resolveValues
} from './local-config.mjs';

const app = 'com.bazaarplusplus.android.dev';
const directory = 'files/dev/login';
const activity = `${app}/com.bazaarplusplus.android.launch.LauncherActivity`;
const help = `Usage: just android::dev-login <serial> [--resume-only]

Read config.ini [game] BPP_GAME_EMAIL and BPP_GAME_PASSWORD, restart the debug
Android App and ask its Mod to submit the game's own login form once. The App
uses its normal Play gate. Success requires this process's authenticated login
and loaded player profile. --resume-only sends no credentials and verifies that
the game restores its saved session after restart.

Requires adb, the updated debug App and this checkout's Android Mod. The serial
is mandatory. Exported BPP_GAME_EMAIL/BPP_GAME_PASSWORD override config.ini.
Credentials travel over stdin to a private, expiring one-use request. They never
appear in arguments, output or host temporary files. The game owns token storage.
The command does not install APKs, reset game data or change the official App.
Only one login command may run on a device. After a forcibly killed command,
confirm it has stopped before removing the debug App's files/dev/login/lock.
`;

function execute(command, args, input, env, label) {
  const result = spawnSync(command, args, {
    input,
    env,
    encoding: 'utf8',
    maxBuffer: 1024 * 1024,
    timeout: 15_000,
    windowsHide: true
  });
  // Never include child output, input or spawn exception messages in an error.
  if (result.error || result.status !== 0)
    throw new Error(
      `${label} failed (${result.error?.code ?? result.status}); device output withheld.`
    );
  return result.stdout;
}

function credentials(env, allowManual, resumeOnly) {
  if (resumeOnly) return { mode: 'resume' };
  const home = configHome(env);
  assertExternal(home);
  const file = path.join(home, 'config.ini');
  if (process.platform !== 'win32' && fs.existsSync(file))
    fs.chmodSync(file, 0o600);
  const values = resolveValues(readConfig(home), 'game', env);
  const email = values.BPP_GAME_EMAIL;
  const password = values.BPP_GAME_PASSWORD;
  if (email?.trim() && password) return { mode: 'login', email, password };
  if (allowManual && !email?.trim() && !password) return { mode: 'manual' };
  throw new Error(
    'Set both BPP_GAME_EMAIL and BPP_GAME_PASSWORD in config.ini [game].'
  );
}

const failures = {
  login_failed: 'The game reported a sign-in error; inspect its login notice.',
  action_required:
    'The game requires account verification or another account action.',
  needs_credentials:
    'The game could not restore its session without credentials.',
  timeout:
    'The game did not finish authentication and load its profile before the deadline.',
  adapter_failed:
    'The development login adapter failed; check the installed App and Mod versions.',
  invalid_request: 'The development login request was rejected.',
  launch_blocked:
    'The launcher could not offer Play; inspect its gate or error.',
  cancelled: 'The development login request was cancelled.'
};

// One process restart and one request. setup-device uses this same operation, including its
// manual sign-in mode, rather than restarting again and inferring authentication from logs.
export async function signIn(
  serial,
  {
    env = process.env,
    run = execute,
    sleep = delay,
    now = Date.now,
    id = randomUUID().replaceAll('-', ''),
    timeout = 300,
    resumeOnly = false,
    allowManual = false,
    signal,
    onProgress = () => {}
  } = {}
) {
  if (!serial || !/^(?!-)[A-Za-z0-9_.:\[\]-]+$/.test(serial))
    throw new Error('Provide an explicit adb device serial; see --help.');
  if (
    !/^[0-9a-f]{32}$/.test(id) ||
    !Number.isFinite(timeout) ||
    timeout < 1 ||
    timeout > 600
  )
    throw new Error('Invalid request id or timeout (1–600 seconds).');
  const request = { id, ...credentials(env, allowManual, resumeOnly) };
  const childEnv = { ...env };
  delete childEnv.BPP_GAME_EMAIL;
  delete childEnv.BPP_GAME_PASSWORD;
  const sdkAdb = path.join(
    env.ANDROID_HOME ??
      path.join(process.env.HOME ?? '', 'Library/Android/sdk'),
    'platform-tools/adb'
  );
  const command = env.ADB || (fs.existsSync(sdkAdb) ? sdkAdb : 'adb');
  const adb = (args, label, input) =>
    run(command, ['-s', serial, ...args], input, childEnv, label);
  const shell = (script, label, input) =>
    adb(['shell', '-T', `run-as ${app} sh -c '${script}'`], label, input);
  const checkCancelled = () => {
    if (signal?.aborted) throw new Error('Development login cancelled.');
  };
  checkCancelled();
  adb(['shell', 'run-as', app, 'true'], 'Debug App access');
  shell(
    `umask 077; mkdir -p ${directory}; mkdir ${directory}/lock`,
    'Claiming device login (another command or an interrupted command may hold its lock)'
  );
  let launched = false;
  let settled = false;
  try {
    checkCancelled();
    adb(['shell', 'am', 'force-stop', app], 'Stopping debug App');
    // Keep a fresh Mod log for setup-device's later injection/hook checks.
    shell(
      'rm -f no_backup/game-external/MelonLoader/MelonLoader/Latest.log',
      'Clearing the previous Mod log'
    );
    request.expiresAt = now() + timeout * 1000;
    const input = JSON.stringify(request);
    if (Buffer.byteLength(input) > 16_384)
      throw new Error('Development login request is too large.');
    shell(
      `umask 077; rm -f ${directory}/result.json; cat > ${directory}/request.tmp && mv ${directory}/request.tmp ${directory}/request.json`,
      'Writing one-use login request',
      input
    );
    delete request.email;
    delete request.password;
    checkCancelled();
    launched = true;
    adb(['shell', 'am', 'start', '-n', activity], 'Opening launcher');
    onProgress(
      request.mode === 'manual'
        ? 'Sign in in the game window; waiting for authentication and profile.'
        : `Waiting for ${request.mode === 'resume' ? 'session restoration' : 'game sign-in'} on ${serial}.`
    );
    let pid = '';
    while (now() < request.expiresAt + 5000) {
      checkCancelled();
      const current = adb(
        ['shell', `pidof ${app} || true`],
        'Checking game process'
      ).trim();
      if (pid && current !== pid)
        throw new Error('The game process exited or changed during login.');
      if (current) pid = current;
      const text = shell(
        `if [ -f ${directory}/result.json ]; then cat ${directory}/result.json; fi`,
        'Reading login result'
      );
      if (text.trim()) {
        let value;
        try {
          value = JSON.parse(text);
        } catch {
          throw new Error('Invalid development login result.');
        }
        if (value.id !== id || !pid || value.pid !== Number(pid))
          throw new Error(
            'Login result does not belong to this request and process.'
          );
        if (
          value.status !== 'signed_in' &&
          !Object.hasOwn(failures, value.status)
        )
          throw new Error('Unknown development login result.');
        if (
          value.status === 'signed_in' &&
          (value.phase !== 'PlayNowValidated' ||
            !/^[A-Za-z0-9_.-]+$/.test(value.gameVersion ?? '') ||
            typeof value.submitted !== 'boolean')
        )
          throw new Error('Incomplete authenticated login result.');
        settled = [
          'signed_in',
          'login_failed',
          'action_required',
          'needs_credentials',
          'launch_blocked'
        ].includes(value.status);
        if (value.status !== 'signed_in')
          throw new Error(failures[value.status]);
        return {
          id,
          pid: value.pid,
          gameVersion: value.gameVersion,
          status: value.status,
          phase: value.phase,
          submitted: value.submitted
        };
      }
      await sleep(1000);
    }
    throw new Error(
      'No login result before the deadline; inspect the launcher, Mod loading and device health.'
    );
  } finally {
    delete request.email;
    delete request.password;
    // Stop an unacknowledged attempt so it cannot submit after timeout or cancellation.
    try {
      if (launched && !settled)
        adb(['shell', 'am', 'force-stop', app], 'Stopping unfinished login');
    } finally {
      shell(
        `rm -f ${directory}/request.json ${directory}/request.tmp ${directory}/result.json ${directory}/result.json.tmp; rmdir ${directory}/lock`,
        'Removing private login request and lock'
      );
    }
  }
}

// Both entry points keep cancellation active while a private login request exists.
export async function withLoginCancellation(action) {
  const abort = new AbortController();
  const cancel = () => abort.abort();
  process.once('SIGINT', cancel);
  process.once('SIGTERM', cancel);
  try {
    return await action(abort.signal);
  } catch (error) {
    if (abort.signal.aborted) error.code = 'LOGIN_CANCELLED';
    throw error;
  } finally {
    process.removeListener('SIGINT', cancel);
    process.removeListener('SIGTERM', cancel);
  }
}

if (
  process.argv[1] &&
  import.meta.url === pathToFileURL(process.argv[1]).href
) {
  try {
    const args = process.argv.slice(2);
    if (args.length === 1 && ['--help', '-h'].includes(args[0]))
      console.log(help);
    else {
      if (
        args.length < 1 ||
        args.length > 2 ||
        (args[1] && args[1] !== '--resume-only')
      )
        throw new Error(help);
      const result = await withLoginCancellation((signal) =>
        signIn(args[0], {
          resumeOnly: args[1] === '--resume-only',
          signal,
          onProgress: console.log
        })
      );
      console.log(JSON.stringify(result));
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = error.code === 'LOGIN_CANCELLED' ? 130 : 1;
  }
}
