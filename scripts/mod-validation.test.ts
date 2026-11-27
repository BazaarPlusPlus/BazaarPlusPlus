import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'vite-plus/test';
import type { TestContext } from 'vite-plus/test';
import { androidChecks, validate, withBuildLock } from './mod-validation.ts';
import type { ValidationReceipt } from './mod-validation.ts';
import { runFixtureGit } from './test-support/git-fixture.ts';

const root = path.resolve(import.meta.dirname, '..');
function fixture(t: TestContext): string {
  const cwd = fs.realpathSync(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-validation-'))
  );
  t.onTestFinished(() => fs.rmSync(cwd, { recursive: true, force: true }));
  return cwd;
}

test('build lock rejects a concurrent command and is released after failure', async (t) => {
  const cwd = fixture(t);
  await assert.rejects(
    withBuildLock(cwd, async (env) => {
      assert.ok(
        fs.existsSync(path.join(env.BPP_MOD_BUILD_LOCK!, 'owner.json'))
      );
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

test('nested build lock dispatch reuses the outer lock', async (t) => {
  const cwd = fixture(t);
  const lockFile = path.join(cwd, 'bazaarplusplus-mod/scripts/build-lock.ts');
  fs.mkdirSync(path.dirname(lockFile), { recursive: true });
  fs.copyFileSync(
    path.join(root, 'bazaarplusplus-mod/scripts/build-lock.ts'),
    lockFile
  );
  const script = path.join(cwd, 'bazaarplusplus-mod/scripts/nested.ts');
  fs.writeFileSync(
    script,
    `import path from 'node:path';
import { withBuildLock } from './build-lock.ts';
const workspace = path.resolve(import.meta.dirname, '../..');
await withBuildLock(workspace, () => {
  console.log('nested build ran');
});
`
  );
  const invoke = (env: NodeJS.ProcessEnv) =>
    spawnSync('bun', [script], {
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
    const git = (...args: string[]): string =>
      runFixtureGit(args, { cwd }).trim();
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
    const calls: (readonly string[])[] = [];
    let active = false;
    const code = await validate(
      cwd,
      process.env,
      async (_command, args, options) => {
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
    const receipt = JSON.parse(
      fs.readFileSync(path.join(out, 'result.json'), 'utf8')
    ) as ValidationReceipt;
    assert.equal(receipt.head, git('rev-parse', 'HEAD'));
    assert.equal(receipt.status, failure ? 'failed' : 'passed');
    assert.equal(receipt.steps.length, calls.length);
    assert.equal(
      fs.readFileSync(receipt.steps[0].log, 'utf8'),
      'gate output\n'
    );
  });
}
