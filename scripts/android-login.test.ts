import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'vitest';
import type { TestContext } from 'vitest';
import { signIn, withLoginCancellation } from './android-login.ts';
import type { LoginRequest, SignInOptions } from './android-login.ts';

const email = 'test+android@example.invalid';
const password = '中文 <&> " \' $() ` %s';
const id = '1234567890abcdef1234567890abcdef';
const serial = 'emulator-5570';
function fixture(
  t: TestContext,
  config = `[game]\nBPP_GAME_EMAIL=${email}\nBPP_GAME_PASSWORD=from-config\n`
): NodeJS.ProcessEnv {
  const home = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-login-'));
  t.onTestFinished(() => fs.rmSync(home, { recursive: true, force: true }));
  fs.writeFileSync(path.join(home, 'config.ini'), config);
  return {
    ...process.env,
    BPP_CONFIG_HOME: home,
    BPP_GAME_EMAIL: '',
    BPP_GAME_PASSWORD: ''
  };
}

interface DeviceOptions {
  status?: string;
  receiptId?: string;
  receiptPid?: number;
  noResult?: boolean;
  busy?: boolean;
  die?: boolean;
}

// One recorded adb call.
interface Call {
  command: string;
  args: string[];
  input: string | undefined;
  env: NodeJS.ProcessEnv;
  label: string;
}

// The ADB boundary supplies a receipt, not a simulation of Unity, coordinates or authentication.
function device({
  status = 'signed_in',
  receiptId = id,
  receiptPid = 42,
  noResult = false,
  busy = false,
  die = false
}: DeviceOptions = {}) {
  const calls: Call[] = [];
  // The request the device received over stdin.
  let request: LoginRequest | undefined;
  let clock = 0;
  let reads = 0;
  return {
    calls,
    get request(): LoginRequest {
      return request!;
    },
    options: {
      id,
      timeout: 5,
      now: () => clock,
      sleep: async (ms) => {
        clock += ms;
      },
      run: (command, args, input, env, label) => {
        calls.push({ command, args, input, env, label });
        assert.deepEqual(args.slice(0, 2), ['-s', serial]);
        if (label.startsWith('Claiming') && busy)
          throw new Error('Device login already active.');
        if (label === 'Writing one-use login request')
          request = JSON.parse(input!) as LoginRequest;
        if (label === 'Checking game process')
          return die && reads ? '' : '42\n';
        if (label === 'Reading login result') {
          reads++;
          return noResult
            ? ''
            : JSON.stringify({
                id: receiptId,
                pid: receiptPid,
                status,
                phase: 'PlayNowValidated',
                gameVersion: '1.0.13084-production-android-4670b82d',
                submitted: request!.mode === 'login'
              });
        }
        return '';
      }
    } satisfies SignInOptions
  };
}

test('sends Unicode credentials only over stdin, starts once, and returns a correlated authenticated receipt', async (t) => {
  const env = fixture(t);
  env.BPP_GAME_PASSWORD = password;
  const d = device();
  const result = await signIn(serial, { env, ...d.options });
  assert.equal(result.status, 'signed_in');
  assert.equal(result.submitted, true);
  assert.equal(d.request.email, email);
  assert.equal(d.request.password, password);
  assert.equal(d.calls.filter((c) => c.label === 'Opening launcher').length, 1);
  assert.equal(
    d.calls.filter((c) => c.label === 'Writing one-use login request').length,
    1
  );
  assert.ok(d.calls.at(-1)!.label.startsWith('Removing private'));
  for (const c of d.calls) {
    assert.ok(!JSON.stringify(c.args).includes(email));
    assert.ok(!JSON.stringify(c.args).includes(password));
    assert.equal(c.env.BPP_GAME_EMAIL, undefined);
    assert.equal(c.env.BPP_GAME_PASSWORD, undefined);
    assert.doesNotMatch(
      c.args.join(' '),
      /uiautomator|input (text|tap|swipe|keyevent)/
    );
  }
  assert.doesNotMatch(
    JSON.stringify(result),
    /example.invalid|from-config|中文/
  );
});

test('resume-only never reads or transmits configured credentials', async (t) => {
  const env = fixture(t, '');
  const d = device();
  const result = await signIn(serial, { env, ...d.options, resumeOnly: true });
  assert.equal(result.submitted, false);
  assert.equal(d.request.mode, 'resume');
  assert.equal(d.request.email, undefined);
  assert.equal(d.request.password, undefined);
});

test('setup can wait for manual login only when both credential fields are absent', async (t) => {
  const env = fixture(t, '[game]\n');
  const d = device();
  await signIn(serial, { env, ...d.options, allowManual: true });
  assert.equal(d.request.mode, 'manual');
  const partial = fixture(t, `[game]\nBPP_GAME_EMAIL=${email}\n`);
  await assert.rejects(
    signIn(serial, {
      env: partial,
      allowManual: true,
      run: () => assert.fail('must not touch device')
    }),
    /Set both/
  );
});

test('stale request or process receipts cannot report success and stop the unfinished process', async (t) => {
  for (const bad of [{ receiptId: 'stale' }, { receiptPid: 100 }]) {
    const d = device(bad);
    await assert.rejects(
      signIn(serial, { env: fixture(t), ...d.options }),
      /does not belong/
    );
    assert.ok(d.calls.some((c) => c.label === 'Stopping unfinished login'));
    assert.ok(d.calls.at(-1)!.label.startsWith('Removing private'));
  }
});

test('a game rejection is terminal and does not cause another submit or restart', async (t) => {
  for (const status of [
    'login_failed',
    'action_required',
    'launch_blocked',
    'needs_credentials'
  ]) {
    const d = device({ status });
    await assert.rejects(
      signIn(serial, { env: fixture(t), ...d.options }),
      (error: Error) => {
        assert.doesNotMatch(error.message, /example.invalid|from-config/);
        return true;
      }
    );
    assert.equal(
      d.calls.filter((c) => c.label === 'Opening launcher').length,
      1
    );
    assert.equal(
      d.calls.filter((c) => c.label === 'Writing one-use login request').length,
      1
    );
    assert.ok(d.calls.at(-1)!.label.startsWith('Removing private'));
  }
});

test('device exit and missing replies stop and clean the attempt instead of retrying', async (t) => {
  for (const opts of [{ noResult: true }, { noResult: true, die: true }]) {
    const d = device(opts);
    await assert.rejects(
      signIn(serial, { env: fixture(t), ...d.options }),
      /deadline|exited/
    );
    assert.ok(d.calls.some((c) => c.label === 'Stopping unfinished login'));
    assert.ok(d.calls.at(-1)!.label.startsWith('Removing private'));
    assert.equal(
      d.calls.filter((c) => c.label === 'Opening launcher').length,
      1
    );
  }
});

test('cancellation removes the request and stops an in-flight login', async (t) => {
  const abort = new AbortController();
  const d = device({ noResult: true });
  await assert.rejects(
    signIn(serial, {
      env: fixture(t),
      ...d.options,
      signal: abort.signal,
      sleep: async () => abort.abort()
    }),
    /cancelled/
  );
  assert.ok(d.calls.some((c) => c.label === 'Stopping unfinished login'));
  assert.ok(d.calls.at(-1)!.label.startsWith('Removing private'));
});

test('an existing device owner is left untouched', async (t) => {
  const d = device({ busy: true });
  await assert.rejects(
    signIn(serial, { env: fixture(t), ...d.options }),
    /already active/
  );
  assert.ok(!d.calls.some((c) => /Stopping|Writing|Removing/.test(c.label)));
});

test('invalid target and missing credentials fail before ADB', async (t) => {
  const env = fixture(t, '[game]\n');
  const run = () => assert.fail('must not touch device');
  for (const target of ['', '-e', 'emulator-5570;echo secret'])
    await assert.rejects(
      signIn(target, { env, run }),
      /explicit adb device serial/
    );
  await assert.rejects(signIn(serial, { env, run }), /Set both/);
});

// A timeout receipt does not cancel the game's native async request by itself.
test('timeout and adapter failure receipts still stop the in-flight game before releasing its lock', async (t) => {
  for (const status of ['timeout', 'adapter_failed', 'cancelled']) {
    const d = device({ status });
    await assert.rejects(signIn(serial, { env: fixture(t), ...d.options }));
    const stop = d.calls.findIndex(
      (c) => c.label === 'Stopping unfinished login'
    );
    assert.ok(stop >= 0 && stop < d.calls.length - 1);
    assert.ok(d.calls.at(-1)!.label.startsWith('Removing private'));
  }
});

test('the shared entry-point signal scope allows login cleanup before reporting cancellation', async (t) => {
  const before = process.listenerCount('SIGINT');
  const d = device({ noResult: true });
  await assert.rejects(
    withLoginCancellation((signal) =>
      signIn(serial, {
        env: fixture(t),
        ...d.options,
        signal,
        sleep: async () => process.emit('SIGINT')
      })
    ),
    (error: NodeJS.ErrnoException) => error.code === 'LOGIN_CANCELLED'
  );
  assert.ok(d.calls.at(-1)!.label.startsWith('Removing private'));
  assert.equal(process.listenerCount('SIGINT'), before);
});
