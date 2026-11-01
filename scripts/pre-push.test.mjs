import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { missingDependencies, pushPlan, pushCommands } from './pre-push.mjs';
import { runFixtureGit } from './test-support/git-fixture.mjs';
import { gitEnvironment } from './git-command.mjs';

const root = path.resolve(import.meta.dirname, '..');
const zero = '0'.repeat(40);
function fixture(t) {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-push-'));
  t.after(() => fs.rmSync(cwd, { recursive: true, force: true }));
  const git = (...args) => runFixtureGit(args, { cwd }).trim();
  git('init', '--template=');
  git('config', 'user.name', 'Test');
  git('config', 'user.email', 'test@example.test');
  const commit = (file) => {
    fs.mkdirSync(path.dirname(path.join(cwd, file)), { recursive: true });
    fs.writeFileSync(path.join(cwd, file), file + Math.random());
    git('add', file);
    git('commit', '-m', 'fixture');
    return git('rev-parse', 'HEAD');
  };
  const base = commit('README.md');
  git('update-ref', 'refs/remotes/upstream/master', base);
  git(
    'symbolic-ref',
    'refs/remotes/upstream/HEAD',
    'refs/remotes/upstream/master'
  );
  const input = (head, before = zero) =>
    `refs/heads/topic ${head} refs/heads/topic ${before}\n`;
  return {
    cwd,
    git,
    commit,
    base,
    input,
    plan: (head, before) =>
      pushCommands(pushPlan(input(head, before), 'upstream', cwd))
  };
}

test('first push uses merge-base even after the default branch advances', (t) => {
  const f = fixture(t);
  const advance = f.commit('bazaarplusplus-site/src/page.ts');
  f.git('update-ref', 'refs/remotes/upstream/master', advance);
  f.git('checkout', '--detach', f.base);
  const head = f.commit('bazaarplusplus-mod/src/panel.cs');
  assert.deepEqual(f.plan(head), [
    ['verify', 'mod::test', 'mod::test-performance']
  ]);
});

test('an Android App push runs only the Android App tests', (t) => {
  const f = fixture(t);
  const head = f.commit(
    'bazaarplusplus-android/app/src/main/AndroidManifest.xml'
  );
  assert.deepEqual(f.plan(head), [['android::test']]);
});

test('later pushes use the remote tip and preserve shared contract consumers', (t) => {
  const f = fixture(t);
  const previous = f.commit('bazaarplusplus-site/src/page.ts');
  const head = f.commit('bazaarplusplus-mod/src/panel.cs');
  assert.deepEqual(f.plan(head, previous), [
    ['verify', 'mod::test', 'mod::test-performance']
  ]);
  const shared = f.commit('bazaarplusplus-server/contracts/v6/fixture.json');
  const commands = f.plan(shared, head);
  for (const gate of ['mod::test', 'server::test', 'analyzer::test'])
    assert.ok(commands.flat().includes(gate));
});

test('a rebased force push selects the branch changes, not the default branch commits it moved onto', (t) => {
  const f = fixture(t);
  const before = f.commit('bazaarplusplus-site/src/page.ts');
  f.git('checkout', '--detach', f.base);
  f.commit('bazaarplusplus-mod/src/panel.cs');
  const master = f.commit('bazaarplusplus-server/src/route.ts');
  f.git('update-ref', 'refs/remotes/upstream/master', master);
  f.git('cherry-pick', before);
  const rebased = f.git('rev-parse', 'HEAD');
  assert.deepEqual(f.plan(rebased, before), [['site::test']]);
  // A lease on a tip this clone never fetched is equally a rewrite.
  assert.deepEqual(f.plan(rebased, 'f'.repeat(40)), [['site::test']]);
  const next = f.commit('bazaarplusplus-site/src/other.ts');
  assert.deepEqual(f.plan(next, rebased), [['site::test']]);
});

test('selected gates name their missing npm dependencies', (t) => {
  const f = fixture(t);
  const commands = pushCommands({
    release: ['x'],
    'mod-macos': ['x'],
    site: ['x']
  });
  assert.deepEqual(missingDependencies(commands, f.cwd), [
    '.',
    'bazaarplusplus-site'
  ]);
  fs.mkdirSync(path.join(f.cwd, 'bazaarplusplus-site/node_modules'), {
    recursive: true
  });
  assert.deepEqual(missingDependencies(commands, f.cwd), ['.']);
});

test('unknown bases run all gates and deletions run none', (t) => {
  const f = fixture(t);
  const head = f.commit('bazaarplusplus-mod/src/panel.cs');
  assert.deepEqual(f.plan(zero, head), []);
  f.git('symbolic-ref', '--delete', 'refs/remotes/upstream/HEAD');
  assert.ok(f.plan(head).flat().includes('installer::test'));
  assert.ok(
    pushCommands(pushPlan('', 'upstream', f.cwd))
      .flat()
      .includes('commands-check')
  );
});

test('real Lefthook forwards stdin and the remote name on first push; gate failure blocks push', (t) => {
  const f = fixture(t);
  const head = f.commit('bazaarplusplus-mod/src/panel.cs');
  for (const file of [
    'lefthook.yml',
    'scripts/pre-push.mjs',
    'scripts/git-command.mjs',
    '.github/scripts/checks-scope.mjs'
  ]) {
    fs.mkdirSync(path.dirname(path.join(f.cwd, file)), { recursive: true });
    fs.copyFileSync(path.join(root, file), path.join(f.cwd, file));
  }
  fs.writeFileSync(
    path.join(f.cwd, 'JUSTFILE'),
    "mod mod 'mod.just'\nverify +recipes:\n    just {{recipes}}\n"
  );
  fs.writeFileSync(
    path.join(f.cwd, 'mod.just'),
    'test:\n    exit 37\ntest-performance:\n    exit 38\n'
  );
  const result = spawnSync(
    process.execPath,
    [
      path.join(root, 'node_modules/lefthook/bin/index.js'),
      'run',
      'pre-push',
      'upstream',
      'unused'
    ],
    {
      cwd: f.cwd,
      input: f.input(head),
      encoding: 'utf8',
      timeout: 30_000,
      env: { ...gitEnvironment(), LEFTHOOK: '1', LEFTHOOK_INTERACTIVE: '0' }
    }
  );
  assert.notEqual(result.status, 0);
  const output = result.stdout + result.stderr;
  assert.match(output, /Push checks: verify mod::test mod::test-performance/);
  assert.match(output, /37/);
});
