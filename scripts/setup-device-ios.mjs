import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const root = path.resolve(import.meta.dirname, '..');
const ios = path.join(root, 'bazaarplusplus-ios');
const app = path.join(
  ios,
  'build/steam-app/simulator/launcher-derived/Build/Products/Release-iphonesimulator/BazaarPlusPlus.app'
);
const evidence = (profile) =>
  path.join(ios, 'build', `setup-device-${profile.name}`);
// One simulator per Core/Ui/ScreenFill branch, matching the Android AVD matrix.
// Runtimes are pinned to the ones that hold the downloaded game on the Mac mini.
export const profiles = [
  {
    name: 'bpp_sim_phone',
    model: 'iPhone 18 Pro Max',
    deviceType: 'com.apple.CoreSimulator.SimDeviceType.iPhone-18-Pro-Max',
    runtime: 'com.apple.CoreSimulator.SimRuntime.iOS-27-0'
  },
  {
    name: 'bpp_sim_duo',
    model: 'iPhone Duo',
    deviceType: 'com.apple.CoreSimulator.SimDeviceType.iPhone-Duo',
    runtime: 'com.apple.CoreSimulator.SimRuntime.iOS-27-2-iPhone19-4',
    // FoldGuard pauses a folded game before RunTasks completes; simctl cannot unfold.
    note: 'unfold it in Device Hub before init; folded, the game pauses and the regression fails'
  },
  {
    name: 'bpp_sim_ipad',
    model: 'iPad Pro 13-inch (M5)',
    deviceType:
      'com.apple.CoreSimulator.SimDeviceType.iPad-Pro-13-inch-M5-12GB',
    runtime: 'com.apple.CoreSimulator.SimRuntime.iOS-27-0'
  }
];
export const help = `  just setup-device ios [NAME|all] [--timeout SECONDS] [--keep-open]

${profiles.map((p) => `  ${p.name.padEnd(17)} ${p.model}, ${p.runtime.split('.').pop()}`).join('\n')}

iOS init defaults to all and a 300-second sign-in wait per simulator. It needs
Xcode with these runtimes and the --test-control simulator build
(python3 tools/build.py simulator --test-control in bazaarplusplus-ios).
A missing simulator is created empty. To reuse one that already holds the game
and sign-in, rename it first: xcrun simctl rename UDID NAME.
Each simulator has its own sandbox and keychain. Init boots it, installs the
build and waits for its saved Steam login; without one it starts a QR sign-in
on the simulator screen, behind the first-run intro: skip the intro, then scan
it with the Steam mobile app, or cancel and sign in with a password there.
It then downloads the game (about 6 GB) and passes when tools/regress.sh
reports RunTasks complete with Failed: 0.
Success shuts down a simulator init booted; --keep-open needs a single NAME.
Failure leaves the simulator open and returns nonzero. Rerun init after repair.
Init drives one simulator at a time through the shared test channel in
bazaarplusplus-ios/build/steam-app/verification/.
Evidence: bazaarplusplus-ios/build/setup-device-NAME/ (private).`;

function run(args, options = {}) {
  const result = spawnSync(args[0], args.slice(1), {
    encoding: 'utf8',
    timeout: 60_000,
    ...options
  });
  if (result.error || result.status !== 0)
    throw new Error(
      `${args.slice(0, 3).join(' ')} failed: ${result.error?.message || result.stderr?.trim() || `exit ${result.status}`}`
    );
  return result.stdout;
}

const simctl = (...args) => run(['xcrun', 'simctl', ...args]);

// Name lookup returns every simulator so a duplicate name fails instead of
// picking one sandbox at random.
export function findDevices(list, name) {
  return Object.entries(list.devices).flatMap(([runtime, devices]) =>
    devices
      .filter((device) => device.name === name)
      .map((device) => ({ ...device, runtime }))
  );
}

export function deviceState(list, profile) {
  const found = findDevices(list, profile.name);
  if (!found.length) return { state: 'missing' };
  if (found.length > 1) return { state: 'duplicate name' };
  const [device] = found;
  return {
    device,
    state:
      device.deviceTypeIdentifier === profile.deviceType &&
      device.runtime === profile.runtime
        ? 'configured'
        : 'config mismatch'
  };
}

const list = () => JSON.parse(simctl('list', 'devices', '-j'));

function snapshot() {
  const result = spawnSync('python3', ['tools/app-control.py', 'snapshot'], {
    cwd: ios,
    encoding: 'utf8',
    timeout: 40_000
  });
  try {
    return result.status === 0 ? JSON.parse(result.stdout) : null;
  } catch {
    return null;
  }
}

const control = (action) =>
  run(['python3', 'tools/app-control.py', action], { cwd: ios });

async function until(seconds, check, message) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    const value = check();
    if (value) return value;
    await delay(2000);
  }
  throw new Error(message);
}

async function initialize(profile, { timeout, keepOpen }) {
  let { state, device } = deviceState(list(), profile);
  if (state === 'missing') {
    console.log(
      `Creating ${profile.name} (${profile.model}); it needs its own sign-in and game download.`
    );
    simctl('create', profile.name, profile.deviceType, profile.runtime);
    ({ state, device } = deviceState(list(), profile));
  }
  if (state !== 'configured')
    throw new Error(
      `${profile.name}: ${state}; expected ${profile.model} on ${profile.runtime}. Existing data was preserved.`
    );
  const udid = device.udid;
  if (profile.note) console.log(`${profile.name}: ${profile.note}.`);
  const out = evidence(profile);
  fs.mkdirSync(out, { recursive: true, mode: 0o700 });
  const receipt = {
    name: profile.name,
    udid,
    startedAt: new Date().toISOString(),
    status: 'running'
  };
  const save = () =>
    fs.writeFileSync(
      path.join(out, 'init.json'),
      JSON.stringify(receipt, null, 2) + '\n'
    );
  save();
  const ownBoot = device.state !== 'Booted';
  try {
    if (ownBoot) simctl('boot', udid);
    run(['xcrun', 'simctl', 'bootstatus', udid, '-b'], { timeout: 300_000 });
    const env = { ...process.env, BPP_DEVICE: udid };
    run(['python3', 'tools/run-app.py'], { cwd: ios, env, timeout: 300_000 });
    console.log(`${profile.name}: waiting ${timeout}s for the Steam sign-in.`);
    let qr = false;
    await until(
      timeout,
      () => {
        const s = snapshot();
        if (!s) return false;
        if (s.loggedIn) return true;
        // The launcher restores a saved login on its own; start QR only without one.
        if (
          !qr &&
          !s.busy &&
          !s.savedAccount &&
          /^(signedOut|error)$/.test(s.state)
        ) {
          control('qr');
          qr = true;
          console.log(
            `${profile.name}: on the simulator screen, skip the first-run intro, then scan the QR code with the Steam mobile app or sign in with a password.`
          );
        }
        return false;
      },
      `${profile.name} is not signed in; finish sign-in on the open simulator, then rerun init.`
    );
    const s = snapshot();
    if (s?.state !== 'ready') {
      control('download');
      console.log(
        `${profile.name}: downloading the game; init waits while it progresses.`
      );
      // A first download takes far longer than the regression allows; fail only on a stall.
      let mark = '';
      let progressedAt = Date.now();
      await until(
        Infinity,
        () => {
          const next = snapshot();
          if (next?.state === 'ready') return true;
          if (next && /^(error|installed|outdated)$/.test(next.state))
            throw new Error(
              `${profile.name} download ended in ${next.state}${next.errorCode ? ` (${next.errorCode})` : ''}.`
            );
          // An unreachable app counts as no progress, so a crash ends in the stall error.
          const current = next ? `${next.state} ${next.downloadedBytes}` : mark;
          if (current !== mark) {
            mark = current;
            progressedAt = Date.now();
          }
          if (Date.now() - progressedAt > 300_000)
            throw new Error(
              `${profile.name} download made no progress for 5 minutes.`
            );
          return false;
        },
        ''
      );
    }
    const regress = spawnSync('bash', ['tools/regress.sh'], {
      cwd: ios,
      env,
      encoding: 'utf8',
      timeout: 1_500_000
    });
    const log = `${regress.stdout || ''}${regress.stderr || ''}`;
    // The regression output can carry Steam ticket lines; keep it private.
    fs.writeFileSync(path.join(out, 'regress.log'), log, { mode: 0o600 });
    const tasks = log.match(/RunTasks complete[^\n]*/)?.[0];
    if (regress.status !== 0 || !/Failed: 0\b/.test(tasks || ''))
      throw new Error(
        `${profile.name} regression failed${regress.error ? `: ${regress.error.message}` : ''}; see ${out}/regress.log`
      );
    receipt.status = 'ready';
    receipt.checkedAt = new Date().toISOString();
    receipt.runTasks = tasks.trim();
    save();
    if (!keepOpen) {
      if (ownBoot) simctl('shutdown', udid);
      else
        simctl(
          'terminate',
          udid,
          run([
            'plutil',
            '-extract',
            'CFBundleIdentifier',
            'raw',
            path.join(app, 'Info.plist')
          ]).trim()
        );
    }
    console.log(`READY ${profile.name} (${udid}): ${tasks.trim()}`);
  } catch (error) {
    receipt.status = 'failed';
    receipt.error = error.message;
    save();
    spawnSync('xcrun', [
      'simctl',
      'io',
      udid,
      'screenshot',
      path.join(out, 'screen.png')
    ]);
    throw error;
  }
}

export function status() {
  let devices;
  try {
    devices = list();
  } catch {
    devices = null;
  }
  for (const profile of profiles) {
    const { state, device } = devices
      ? deviceState(devices, profile)
      : { state: 'simctl unavailable' };
    const receipt = path.join(evidence(profile), 'init.json');
    const previous = fs.existsSync(receipt)
      ? JSON.parse(fs.readFileSync(receipt, 'utf8'))
      : null;
    console.log(
      `${profile.name}: ${state}${device ? ` ${device.udid}` : ''}; last init ${previous?.status || 'not run'}${previous?.checkedAt ? ` at ${previous.checkedAt}` : ''} (current login unverified)`
    );
  }
}

export async function init(rest) {
  let name = 'all',
    timeout = 300,
    keepOpen = false;
  if (rest[0] && !rest[0].startsWith('--')) name = rest.shift();
  while (rest.length) {
    const flag = rest.shift();
    if (flag === '--timeout') timeout = Number(rest.shift());
    else if (flag === '--keep-open') keepOpen = true;
    else throw new Error(`Unknown option ${flag}; see --help.`);
  }
  if (!Number.isFinite(timeout) || timeout <= 0)
    throw new Error('Provide positive --timeout seconds.');
  const selected =
    name === 'all'
      ? profiles
      : profiles.filter((profile) => profile.name === name);
  if (!selected.length || (keepOpen && selected.length !== 1))
    throw new Error(
      'Choose a simulator NAME; --keep-open requires a single NAME. See --help.'
    );
  if (process.platform !== 'darwin')
    throw new Error('The iOS simulator matrix requires macOS with Xcode.');
  if (!fs.existsSync(app))
    throw new Error(
      'No simulator build. Run python3 tools/build.py simulator --test-control in bazaarplusplus-ios first.'
    );
  for (const profile of selected)
    await initialize(profile, { timeout, keepOpen });
}
