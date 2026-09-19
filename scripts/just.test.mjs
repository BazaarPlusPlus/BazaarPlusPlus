import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const root = path.resolve(import.meta.dirname, '..');
const just = process.env.JUST_BIN ?? 'just';
const projects = ['mod', 'installer', 'site', 'server', 'analyzer'];
// Module files as the root JUSTFILE declares them, so the fixture follows it.
const moduleFiles = [
  ...fs
    .readFileSync(path.join(root, 'JUSTFILE'), 'utf8')
    .matchAll(/^mod \w+ '([^']+)'$/gm)
].map((match) => match[1]);
const modScripts = ['build', 'test', 'game'];
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
  for (const file of ['JUSTFILE', ...moduleFiles]) {
    fs.mkdirSync(path.dirname(path.join(dir, file)), { recursive: true });
    fs.copyFileSync(path.join(root, file), path.join(dir, file));
  }
  const bin = path.join(dir, 'bin');
  fs.mkdirSync(bin);
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
  for (const tool of ['node', 'npm', 'uv', 'dotnet']) {
    fs.writeFileSync(path.join(bin, tool), stub(tool), { mode: 0o755 });
  }
  const modDir = path.join(dir, 'bazaarplusplus-mod');
  fs.mkdirSync(path.join(modDir, 'scripts'));
  for (const script of modScripts) {
    fs.writeFileSync(
      path.join(modDir, 'scripts', `${script}.sh`),
      stub(`mod-${script}`)
    );
  }

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

const modFmtCheck = (dir) => [
  call(dir, 'mod', 'dotnet', 'tool', 'restore'),
  call(dir, 'mod', 'dotnet', 'csharpier', 'check', '.')
];
const modLocksCheck = (dir) =>
  [
    'src/BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj',
    'src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj',
    'src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj',
    'src/BazaarPlusPlus/BazaarPlusPlus.csproj'
  ].map((project) =>
    call(dir, 'mod', 'dotnet', 'restore', project, '--locked-mode')
  );

test('the default lists every module, including from a project directory', (t) => {
  const f = fixture(t);
  const result = f.run([], { cwd: path.join(f.dir, 'bazaarplusplus-site') });
  succeeded(result);
  for (const module of [...projects, 'release']) {
    assert.match(result.stdout, new RegExp(`^    ${module}:$`, 'm'));
  }
  assert.match(result.stdout, /promote/);
  assert.match(result.stdout, /deploy \*args/);
  assert.deepEqual(f.calls(), []);
});

test('check delegates source-only gates in their project directories', (t) => {
  const f = fixture(t);
  succeeded(f.run(['check']));
  assert.deepEqual(f.calls(), [
    call(f.dir, null, ...rootPrettier('--check')),
    call(f.dir, null, 'node', '--test', 'scripts/just.test.mjs'),
    call(f.dir, null, 'node', 'release.mjs', 'check'),
    ...modFmtCheck(f.dir),
    call(f.dir, 'mod', 'mod-build', 'build'),
    ...modLocksCheck(f.dir),
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
    call(f.dir, 'mod', 'mod-test', 'test'),
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
    call(f.dir, 'mod', 'dotnet', 'tool', 'restore'),
    call(f.dir, 'mod', 'dotnet', 'csharpier', 'format', '.'),
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
  const result = f.run(['check'], { fail: 'mod-build' });
  assert.equal(result.status, 37, result.stdout + result.stderr);
  assert.deepEqual(
    f.calls().map(({ tool }) => tool),
    ['npm', 'node', 'node', 'dotnet', 'dotnet', 'mod-build']
  );
});

for (const [recipe, project, script] of [
  ['installer::dev', 'installer', 'dev'],
  ['site::dev', 'site', 'dev'],
  ['server::dev', 'server', 'dev'],
  ['site::build', 'site', 'build']
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

for (const [recipe, script, command] of [
  ['build', 'build', 'build'],
  ['deploy', 'build', 'deploy'],
  ['test', 'test', 'test'],
  ['test-compat', 'test', 'test-compat'],
  ['fetch-data', 'build', 'fetch-data']
]) {
  test(`mod::${recipe} preserves argument boundaries`, (t) => {
    const f = fixture(t);
    succeeded(f.run([`mod::${recipe}`, '--fast', managedPath]));
    assert.deepEqual(f.calls(), [
      call(f.dir, 'mod', `mod-${script}`, command, '--fast', managedPath)
    ]);
  });
}

test('mod module recipes also accept the subcommand spelling', (t) => {
  const f = fixture(t);
  succeeded(f.run(['mod', 'build', managedPath]));
  assert.deepEqual(f.calls(), [
    call(f.dir, 'mod', 'mod-build', 'build', managedPath)
  ]);
});

test('mod::decompile requires an online or ptr channel before running anything', (t) => {
  const f = fixture(t);
  assert.notEqual(f.run(['mod::decompile', 'Assembly-CSharp']).status, 0);
  assert.deepEqual(f.calls(), []);
  succeeded(f.run(['mod::decompile', 'ptr', 'all']));
  assert.deepEqual(f.calls(), [
    call(f.dir, 'mod', 'mod-game', 'decompile', 'ptr', 'all')
  ]);
});

for (const command of ['sync', 'check', 'promote']) {
  test(`release::${command} delegates exactly once to the product coordinator`, (t) => {
    const f = fixture(t);
    succeeded(
      f.run([`release::${command}`], {
        cwd: path.join(f.dir, 'bazaarplusplus-mod')
      })
    );
    assert.deepEqual(f.calls(), [
      call(f.dir, null, 'node', 'release.mjs', command)
    ]);
  });
}

for (const command of ['prepare', 'build', 'upload']) {
  test(`release::${command} requires an explicit platform before running anything`, (t) => {
    const f = fixture(t);
    assert.notEqual(f.run([`release::${command}`]).status, 0);
    assert.notEqual(f.run([`release::${command}`, 'linux']).status, 0);
    assert.deepEqual(f.calls(), []);
  });

  for (const platform of ['macos', 'windows']) {
    test(`release::${command} ${platform} does not imply other release stages`, (t) => {
      const f = fixture(t);
      succeeded(f.run([`release::${command}`, platform]));
      const args = ['release.mjs', command, '--platform', platform];
      if (command !== 'upload') args.push('--');
      assert.deepEqual(f.calls(), [call(f.dir, null, 'node', ...args)]);
    });
  }

  if (command !== 'upload') {
    test(`release::${command} forwards MSBuild properties without shell expansion`, (t) => {
      const f = fixture(t);
      succeeded(f.run([`release::${command}`, 'windows', managedPath]));
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
