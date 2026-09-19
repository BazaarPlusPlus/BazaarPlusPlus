import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const root = path.resolve(import.meta.dirname, '..');
const just = process.env.JUST_BIN ?? 'just';
const projects = ['mod', 'installer', 'site', 'server', 'analyzer'];
const shellQuote = (value) => `'${value.replaceAll("'", "'\\''")}'`;
const rootPrettier = (mode) => [
  'npm',
  '--prefix',
  'bazaarplusplus-installer',
  'exec',
  '--',
  'prettier',
  '--config',
  'bazaarplusplus-installer/.prettierrc.json',
  mode,
  'release.mjs',
  'release/**/*.{mjs,json}',
  'scripts/**/*.mjs'
];

function fixture(t) {
  const dir = fs.realpathSync(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-just-'))
  );
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  fs.copyFileSync(path.join(root, 'JUSTFILE'), path.join(dir, 'JUSTFILE'));
  const bin = path.join(dir, 'bin');
  fs.mkdirSync(bin);
  for (const project of projects) {
    fs.mkdirSync(path.join(dir, `bazaarplusplus-${project}`));
  }
  const log = path.join(dir, 'calls.jsonl');
  const recorder = path.join(dir, 'record.mjs');
  fs.writeFileSync(
    recorder,
    `import fs from 'node:fs';
const [tool, ...args] = process.argv.slice(2);
fs.appendFileSync(process.env.BPP_JUST_TEST_LOG, JSON.stringify({tool, args, cwd: process.cwd()}) + '\\n');
if (process.env.BPP_JUST_TEST_FAIL === tool) process.exit(37);
`
  );
  const stub = (tool) =>
    `#!/usr/bin/env bash\nexec ${shellQuote(process.execPath)} ${shellQuote(recorder)} ${shellQuote(tool)} "$@"\n`;
  for (const tool of ['node', 'npm', 'uv']) {
    fs.writeFileSync(path.join(bin, tool), stub(tool), { mode: 0o755 });
  }
  fs.writeFileSync(path.join(dir, 'bazaarplusplus-mod', 'run.sh'), stub('mod'));

  return {
    dir,
    run(args = [], { cwd = dir, fail } = {}) {
      return spawnSync(just, ['--color', 'never', ...args], {
        cwd,
        encoding: 'utf8',
        timeout: 30_000,
        env: {
          ...process.env,
          PATH: `${bin}${path.delimiter}${process.env.PATH}`,
          BPP_JUST_TEST_LOG: log,
          BPP_JUST_TEST_FAIL: fail ?? ''
        }
      });
    },
    calls() {
      return fs.existsSync(log)
        ? fs.readFileSync(log, 'utf8').trim().split('\n').map(JSON.parse)
        : [];
    }
  };
}

function succeeded(result) {
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
}

const call = (dir, project, tool, ...args) => ({
  tool,
  args,
  cwd: project ? path.join(dir, `bazaarplusplus-${project}`) : dir
});

test('the default only lists commands, including from a project directory', (t) => {
  const f = fixture(t);
  const result = f.run([], { cwd: path.join(f.dir, 'bazaarplusplus-site') });
  succeeded(result);
  assert.match(result.stdout, /release-promote/);
  assert.match(result.stdout, /site-dev/);
  assert.deepEqual(f.calls(), []);
});

test('check delegates source-only gates in their project directories', (t) => {
  const f = fixture(t);
  succeeded(f.run(['check']));
  assert.deepEqual(f.calls(), [
    call(f.dir, null, ...rootPrettier('--check')),
    call(f.dir, null, 'node', '--test', 'scripts/just.test.mjs'),
    call(f.dir, null, 'node', 'release.mjs', 'check'),
    call(f.dir, 'mod', 'mod', 'format-check'),
    call(f.dir, 'mod', 'mod', 'build', '--no-deploy'),
    call(f.dir, 'installer', 'npm', 'run', 'verify', '--', '--source-only'),
    call(f.dir, 'installer', 'npm', 'run', 'docs:check'),
    ...['typecheck', 'lint', 'format:check', 'build'].map((script) =>
      call(f.dir, 'site', 'npm', 'run', script)
    ),
    call(f.dir, 'server', 'npm', 'run', 'check'),
    call(
      f.dir,
      'analyzer',
      'uv',
      'run',
      '--locked',
      'ruff',
      'format',
      '--check',
      '.'
    ),
    call(f.dir, 'analyzer', 'uv', 'run', '--locked', 'ruff', 'check', '.'),
    call(f.dir, 'analyzer', 'uv', 'run', '--locked', 'ty', 'check')
  ]);
});

test('test runs each suite without a release or publication command', (t) => {
  const f = fixture(t);
  succeeded(f.run(['test']));
  assert.deepEqual(f.calls(), [
    call(f.dir, null, ...rootPrettier('--check')),
    call(f.dir, null, 'node', '--test', 'scripts/just.test.mjs'),
    call(f.dir, 'mod', 'mod', 'test'),
    ...['installer', 'site', 'server'].map((project) =>
      call(f.dir, project, 'npm', 'test')
    ),
    call(f.dir, 'analyzer', 'uv', 'run', '--locked', 'pytest')
  ]);
});

test('fmt formats every project, then re-projects release files', (t) => {
  const f = fixture(t);
  succeeded(f.run(['fmt']));
  assert.deepEqual(f.calls(), [
    call(f.dir, null, ...rootPrettier('--write')),
    call(f.dir, 'mod', 'mod', 'format'),
    ...['installer', 'site', 'server'].map((project) =>
      call(f.dir, project, 'npm', 'run', 'format')
    ),
    call(
      f.dir,
      'analyzer',
      'uv',
      'run',
      '--locked',
      'ruff',
      'check',
      '--fix',
      '.'
    ),
    call(f.dir, 'analyzer', 'uv', 'run', '--locked', 'ruff', 'format', '.'),
    call(f.dir, null, 'node', 'release.mjs', 'sync')
  ]);
});

test('a failing project stops the aggregate and preserves its exit code', (t) => {
  const f = fixture(t);
  const result = f.run(['check'], { fail: 'mod' });
  assert.equal(result.status, 37, result.stdout + result.stderr);
  assert.deepEqual(
    f.calls().map(({ tool }) => tool),
    ['npm', 'node', 'node', 'mod']
  );
});

for (const [recipe, project, script] of [
  ['installer-dev', 'installer', 'dev'],
  ['site-dev', 'site', 'dev'],
  ['server-dev', 'server', 'dev'],
  ['site-build', 'site', 'build']
]) {
  test(`${recipe} delegates only to its local npm script`, (t) => {
    const f = fixture(t);
    succeeded(f.run([recipe]));
    assert.deepEqual(f.calls(), [call(f.dir, project, 'npm', 'run', script)]);
  });
}

// These must remain literal arguments, not interpolated shell source.
const managedPath =
  "-p:ManagedPath=C:/Games/O'Brien/${USER}/$(printf injected); & Managed";

for (const command of ['build', 'test']) {
  test(`mod-${command} preserves argument boundaries and safe build flags`, (t) => {
    const f = fixture(t);
    succeeded(f.run([`mod-${command}`, managedPath]));
    const args = command === 'build' ? ['build', '--no-deploy'] : ['test'];
    assert.deepEqual(f.calls(), [
      call(f.dir, 'mod', 'mod', ...args, managedPath)
    ]);
  });
}

for (const command of ['sync', 'check', 'promote']) {
  test(`release-${command} delegates exactly once to the product coordinator`, (t) => {
    const f = fixture(t);
    succeeded(
      f.run([`release-${command}`], {
        cwd: path.join(f.dir, 'bazaarplusplus-mod')
      })
    );
    assert.deepEqual(f.calls(), [
      call(f.dir, null, 'node', 'release.mjs', command)
    ]);
  });
}

for (const command of ['prepare', 'build', 'upload']) {
  test(`release-${command} requires an explicit platform before running anything`, (t) => {
    const f = fixture(t);
    const result = f.run([`release-${command}`]);
    assert.notEqual(result.status, 0);
    assert.deepEqual(f.calls(), []);
  });

  for (const platform of ['macos', 'windows']) {
    test(`release-${command} ${platform} does not imply other release stages`, (t) => {
      const f = fixture(t);
      succeeded(f.run([`release-${command}`, platform]));
      const args = ['release.mjs', command, '--platform', platform];
      if (command !== 'upload') args.push('--');
      assert.deepEqual(f.calls(), [call(f.dir, null, 'node', ...args)]);
    });
  }

  if (command !== 'upload') {
    test(`release-${command} forwards MSBuild properties without shell expansion`, (t) => {
      const f = fixture(t);
      succeeded(f.run([`release-${command}`, 'windows', managedPath]));
      assert.deepEqual(f.calls(), [
        call(
          f.dir,
          null,
          'node',
          'release.mjs',
          command,
          '--platform',
          'windows',
          '--',
          managedPath
        )
      ]);
    });
  }
}
