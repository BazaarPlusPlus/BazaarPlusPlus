import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { androidChecks, validate, withBuildLock } from './mod-validation.mjs';
import { runFixtureGit } from './test-support/git-fixture.mjs';

const root = path.resolve(import.meta.dirname, '..');
function fixture(t) {
  const cwd = fs.realpathSync(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-validation-'))
  );
  t.after(() => fs.rmSync(cwd, { recursive: true, force: true }));
  return cwd;
}

test('build lock rejects a concurrent command and is released after failure', async (t) => {
  const cwd = fixture(t);
  await assert.rejects(
    withBuildLock(cwd, async (env) => {
      assert.ok(fs.existsSync(path.join(env.BPP_MOD_BUILD_LOCK, 'owner.json')));
      await assert.rejects(
        withBuildLock(cwd, () => assert.fail('ran concurrently')),
        /already locked/
      );
      throw new Error('build failed');
    }),
    /build failed/
  );
  await withBuildLock(cwd, () => {});
  assert.equal(
    fs.existsSync(path.join(cwd, 'bazaarplusplus-mod/artifacts/build.lock')),
    false
  );
});

test('nested shell build dispatch reuses the outer lock', async (t) => {
  const cwd = fixture(t);
  for (const file of [
    'scripts/mod-validation.mjs',
    'scripts/git-command.mjs',
    'bazaarplusplus-mod/scripts/lib.sh'
  ]) {
    fs.mkdirSync(path.dirname(path.join(cwd, file)), { recursive: true });
    fs.copyFileSync(path.join(root, file), path.join(cwd, file));
  }
  fs.writeFileSync(
    path.join(cwd, 'bazaarplusplus-mod/scripts/build.sh'),
    'source "$(dirname "$0")/lib.sh"\ncmd_build() { echo "nested build ran"; }\ndispatch "$@"\n'
  );
  const script = 'bazaarplusplus-mod/scripts/build.sh';
  const invoke = (env) =>
    spawnSync('bash', [script, 'build'], {
      cwd,
      env,
      encoding: 'utf8',
      timeout: 10_000
    });
  const direct = invoke(process.env);
  assert.equal(direct.status, 0, direct.stderr);
  await withBuildLock(cwd, (env) => {
    const nested = invoke(env);
    assert.equal(nested.status, 0, nested.stderr);
    assert.match(nested.stdout, /nested build ran/);
    const concurrent = invoke(process.env);
    assert.notEqual(concurrent.status, 0);
    assert.match(concurrent.stderr, /already locked/);
  });
});

for (const failure of [null, 2]) {
  test(`validation is serial and records ${failure === null ? 'success' : 'the first failure'}`, async (t) => {
    const cwd = fixture(t);
    const git = (...args) => runFixtureGit(args, { cwd }).trim();
    git('init', '--template=');
    git(
      '-c',
      'user.name=Test',
      '-c',
      'user.email=test@example.test',
      'commit',
      '--allow-empty',
      '-m',
      'fixture'
    );
    const calls = [];
    let active = false;
    const code = await validate(
      cwd,
      process.env,
      async (command, args, options) => {
        assert.equal(active, false);
        active = true;
        calls.push(args);
        fs.writeSync(options.stdio[1], 'gate output\n');
        await new Promise((resolve) => setTimeout(resolve, 5));
        active = false;
        return calls.length === failure ? 37 : 0;
      }
    );
    assert.equal(code, failure ? 37 : 0);
    assert.deepEqual(
      calls,
      androidChecks.slice(0, failure ?? androidChecks.length)
    );
    const base = path.join(cwd, 'bazaarplusplus-mod/artifacts/validation');
    const out = path.join(base, fs.readdirSync(base)[0]);
    const receipt = JSON.parse(fs.readFileSync(path.join(out, 'result.json')));
    assert.equal(receipt.head, git('rev-parse', 'HEAD'));
    assert.equal(receipt.status, failure ? 'failed' : 'passed');
    assert.equal(receipt.steps.length, calls.length);
    assert.equal(
      fs.readFileSync(receipt.steps[0].log, 'utf8'),
      'gate output\n'
    );
  });
}
