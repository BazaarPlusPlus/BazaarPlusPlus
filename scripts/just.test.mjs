import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import util from 'node:util';
import { test } from 'node:test';
import { gitEnvironment } from './git-command.mjs';
import { runFixtureGit } from './test-support/git-fixture.mjs';

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
  'exec',
  '--',
  'prettier',
  '--config',
  '.prettierrc.json',
  mode,
  'package.json',
  'package-lock.json',
  '.prettierrc.json',
  'release.mjs',
  'release/**/*.{mjs,ts,json}',
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
  for (const tool of ['node', 'npm', 'uv', 'dotnet', 'cargo']) {
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

const releaseArgs = (command, ...args) => {
  const profile =
    command === 'build'
      ? 'signing'
      : ['upload', 'mirror', 'mirror-all', 'promote'].includes(command)
        ? 'release'
        : null;
  return [
    ...(profile ? ['scripts/workspace.mjs', 'run', profile, '--', 'node'] : []),
    'release.mjs',
    command,
    ...args
  ];
};

const call = (dir, project, tool, ...args) => ({
  tool,
  args,
  cwd: project ? path.join(dir, `bazaarplusplus-${project}`) : dir
});

test('the default lists every module, including from a project directory', (t) => {
  const f = fixture(t);
  const result = f.run([], { cwd: path.join(f.dir, 'bazaarplusplus-site') });
  succeeded(result);
  for (const module of [...projects, 'release']) {
    assert.match(result.stdout, new RegExp(`^    ${module}:$`, 'm'));
  }
  assert.match(result.stdout, /promote/);
  assert.match(result.stdout, /build \*args/);
  assert.deepEqual(f.calls(), []);
});

// The aggregate recipes are checked by property, not by restating every
// project's recipe: release publication never runs, every project gate runs in
// its own directory, and nothing runs anywhere else.
const publishingVerbs = new Set([
  'prepare',
  'build',
  'upload',
  'mirror',
  'mirror-all',
  'promote'
]);
const releaseVerb = ({ tool, args }) => {
  const index = tool === 'node' ? args.indexOf('release.mjs') : -1;
  return index === -1 ? null : args[index + 1];
};
const projectDir = (dir, project) =>
  path.join(dir, `bazaarplusplus-${project}`);

function assertProjectGates(dir, calls) {
  const dirs = new Set([dir, ...projects.map((p) => projectDir(dir, p))]);
  for (const entry of calls) {
    assert.ok(
      dirs.has(entry.cwd),
      `ran outside a project: ${JSON.stringify(entry)}`
    );
    assert.ok(
      !publishingVerbs.has(releaseVerb(entry)),
      `publishing command: ${JSON.stringify(entry)}`
    );
  }
  for (const project of projects)
    assert.ok(
      calls.some((entry) => entry.cwd === projectDir(dir, project)),
      `${project} did not run in its directory`
    );
}

const hasCall = (calls, expected) =>
  calls.some((entry) => util.isDeepStrictEqual(entry, expected));

test('check delegates source-only gates in their project directories', (t) => {
  const f = fixture(t);
  succeeded(f.run(['check']));
  const calls = f.calls();
  assertProjectGates(f.dir, calls);
  assert.ok(hasCall(calls, call(f.dir, null, 'node', 'release.mjs', 'check')));
  assert.ok(hasCall(calls, call(f.dir, null, ...rootPrettier('--check'))));
});

test('test runs each suite without a release or publication command', (t) => {
  const f = fixture(t);
  succeeded(f.run(['test']));
  const calls = f.calls();
  assertProjectGates(f.dir, calls);
  assert.deepEqual(calls.filter(releaseVerb), []);
  assert.ok(hasCall(calls, call(f.dir, null, 'npm', 'test')));
});

test('fmt formats every project, then re-projects release files', (t) => {
  const f = fixture(t);
  succeeded(f.run(['fmt']));
  const calls = f.calls();
  assertProjectGates(f.dir, calls);
  assert.deepEqual(calls[0], call(f.dir, null, ...rootPrettier('--write')));
  assert.deepEqual(
    calls.at(-1),
    call(f.dir, null, 'node', 'release.mjs', 'sync')
  );
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
  ['site::build', 'site', 'build']
]) {
  test(`${recipe} delegates only to its local npm script`, (t) => {
    const f = fixture(t);
    succeeded(f.run([recipe]));
    assert.deepEqual(f.calls(), [call(f.dir, project, 'npm', 'run', script)]);
  });
}

test('site::e2e passes its mode as the Playwright project, then extra arguments', (t) => {
  const f = fixture(t);
  succeeded(f.run(['site::e2e']));
  succeeded(f.run(['site::e2e', 'golden', '--update-snapshots']));
  succeeded(f.run(['site::e2e', 'live']));
  assert.deepEqual(f.calls(), [
    call(f.dir, 'site', 'npm', 'run', 'e2e', '--', '--project=golden'),
    call(
      f.dir,
      'site',
      'npm',
      'run',
      'e2e',
      '--',
      '--project=golden',
      '--update-snapshots'
    ),
    call(f.dir, 'site', 'npm', 'run', 'e2e', '--', '--project=live')
  ]);
});

for (const [recipe, project, ...args] of [
  ['server::dev', 'server', 'npm', 'run', 'dev'],
  [
    'analyzer::cli',
    'analyzer',
    'uv',
    'run',
    '--locked',
    'bpp',
    'status',
    '--json'
  ]
]) {
  test(`${recipe} refreshes the scoped configuration in its project directory`, (t) => {
    const f = fixture(t);
    succeeded(
      f.run([recipe, ...(project === 'analyzer' ? ['status', '--json'] : [])])
    );
    assert.deepEqual(f.calls(), [
      call(
        f.dir,
        project,
        'node',
        '../scripts/workspace.mjs',
        'run',
        project,
        '--',
        ...args
      )
    ]);
  });
}

test('setup and scoped commands preserve arguments without shell expansion', (t) => {
  const f = fixture(t);
  const source = '/old checkout/$(touch BAD)';
  succeeded(f.run(['setup', '--from', source, '--skip-deps']));
  succeeded(f.run(['doctor']));
  succeeded(f.run(['with-config', 'mod', 'just', 'mod::build', source]));
  assert.deepEqual(f.calls(), [
    call(
      f.dir,
      null,
      'node',
      'scripts/workspace.mjs',
      'setup',
      '--from',
      source,
      '--skip-deps'
    ),
    call(f.dir, null, 'node', 'scripts/workspace.mjs', 'doctor'),
    call(
      f.dir,
      null,
      'node',
      'scripts/workspace.mjs',
      'run',
      'mod',
      '--',
      'just',
      'mod::build',
      source
    )
  ]);
});

// These must remain literal arguments, not interpolated shell source.
const managedPath =
  "-p:ManagedPath=C:/Games/O'Brien/${USER}/$(printf injected); & Managed";

for (const [recipe, script, command] of [
  ['build', 'build', 'build'],
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

test('mod::decompile requires a snapshot channel before running anything', (t) => {
  const f = fixture(t);
  assert.notEqual(f.run(['mod::decompile', 'Assembly-CSharp']).status, 0);
  assert.deepEqual(f.calls(), []);
  succeeded(f.run(['mod::decompile', 'ptr', 'all']));
  succeeded(f.run(['mod::decompile', 'staging']));
  assert.deepEqual(f.calls(), [
    call(f.dir, 'mod', 'mod-game', 'decompile', 'ptr', 'all'),
    call(f.dir, 'mod', 'mod-game', 'decompile', 'staging')
  ]);
});

// Snapshot Lock recipes: fetch and lock-check never load credentials, publish
// runs inside the release profile, and every one of them refuses an unknown
// platform or channel before any script runs.
test('mod snapshot recipes route to game.sh with validated platform and channel', (t) => {
  const f = fixture(t);
  for (const bad of [
    ['mod::fetch', 'linux', 'online'],
    ['mod::fetch', 'macos', 'beta'],
    ['mod::publish', 'macos'],
    ['mod::publish', 'windows', 'nightly']
  ]) {
    assert.notEqual(f.run(bad).status, 0, bad.join(' '));
  }
  assert.deepEqual(f.calls(), []);
  succeeded(f.run(['mod::snapshot']));
  succeeded(f.run(['mod::fetch', 'windows', 'ptr']));
  succeeded(f.run(['mod::lock-check']));
  succeeded(f.run(['mod::publish', 'macos', 'staging']));
  assert.deepEqual(f.calls(), [
    call(f.dir, 'mod', 'mod-game', 'snapshot'),
    call(f.dir, 'mod', 'mod-game', 'fetch', 'windows', 'ptr'),
    call(f.dir, 'mod', 'mod-game', 'lock-check'),
    call(
      f.dir,
      'mod',
      'node',
      '../scripts/workspace.mjs',
      'run',
      'release',
      '--',
      'bash',
      'scripts/game.sh',
      'publish',
      'macos',
      'staging'
    )
  ]);
});

test('mod::matrix forwards the named channels to build.sh', (t) => {
  const f = fixture(t);
  succeeded(f.run(['mod::matrix']));
  succeeded(f.run(['mod::matrix', 'staging', 'ptr']));
  assert.deepEqual(f.calls(), [
    call(f.dir, 'mod', 'mod-build', 'matrix'),
    call(f.dir, 'mod', 'mod-build', 'matrix', 'staging', 'ptr')
  ]);
});

test('mod::check ends with the snapshot lock check', (t) => {
  const f = fixture(t);
  succeeded(f.run(['mod::check']));
  assert.deepEqual(
    f.calls().at(-1),
    call(f.dir, 'mod', 'mod-game', 'lock-check')
  );
});

for (const command of ['sync', 'check', 'verify-mirror', 'promote']) {
  test(`release::${command} delegates exactly once to the product coordinator`, (t) => {
    const f = fixture(t);
    succeeded(
      f.run([`release::${command}`], {
        cwd: path.join(f.dir, 'bazaarplusplus-mod')
      })
    );
    assert.deepEqual(f.calls(), [
      call(f.dir, null, 'node', ...releaseArgs(command))
    ]);
  });
}

for (const [command, ...flags] of [
  ['verify-mirror', '--latest'],
  ['promote', '--without-mainland-mirror'],
  ['promote', '--platform=macos'],
  ['promote', '--platform', 'macos'],
  ['verify-mirror', '--latest', '--platform', 'windows']
]) {
  test(`release::${command} forwards ${flags.join(' ')} to the product coordinator unchanged`, (t) => {
    const f = fixture(t);
    succeeded(f.run([`release::${command}`, ...flags]));
    assert.deepEqual(f.calls(), [
      call(f.dir, null, 'node', ...releaseArgs(command, ...flags))
    ]);
  });
}

test('release::mirror-all forwards both share URLs and optional flags', (t) => {
  const f = fixture(t);
  assert.notEqual(f.run(['release::mirror-all']).status, 0);
  assert.notEqual(
    f.run(['release::mirror-all', 'https://mirror.example/win']).status,
    0
  );
  assert.deepEqual(f.calls(), []);
  const windows = 'https://mirror.example/win?label=windows&key=1';
  const macos = 'https://mirror.example/mac?label=mac%20os&key=2';
  succeeded(
    f.run(['release::mirror-all', windows, macos, '--allow-unverified-mirror'])
  );
  assert.deepEqual(f.calls(), [
    call(
      f.dir,
      null,
      'node',
      ...releaseArgs(
        'mirror-all',
        '--windows-url',
        windows,
        '--macos-url',
        macos,
        '--allow-unverified-mirror'
      )
    )
  ]);
});

test('release::mirror forwards the platform, the share URL and optional flags', (t) => {
  const f = fixture(t);
  assert.notEqual(f.run(['release::mirror']).status, 0);
  assert.notEqual(f.run(['release::mirror', 'macos']).status, 0);
  assert.notEqual(
    f.run(['release::mirror', 'linux', 'https://mirror.example/x']).status,
    0
  );
  assert.deepEqual(f.calls(), []);
  succeeded(f.run(['release::mirror', 'macos', 'https://mirror.example/mac']));
  succeeded(
    f.run([
      'release::mirror',
      'windows',
      'https://mirror.example/win',
      '--allow-unverified-mirror'
    ])
  );
  assert.deepEqual(f.calls(), [
    call(
      f.dir,
      null,
      'node',
      'scripts/workspace.mjs',
      'run',
      'release',
      '--',
      'node',
      'release.mjs',
      'mirror',
      '--platform',
      'macos',
      '--url',
      'https://mirror.example/mac'
    ),
    call(
      f.dir,
      null,
      'node',
      'scripts/workspace.mjs',
      'run',
      'release',
      '--',
      'node',
      'release.mjs',
      'mirror',
      '--platform',
      'windows',
      '--url',
      'https://mirror.example/win',
      '--allow-unverified-mirror'
    )
  ]);
});

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
      const args = releaseArgs(command, '--platform', platform);
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
          ...releaseArgs(command, '--platform', 'windows', '--', managedPath)
        )
      ]);
    });
  }
}

for (const [recipe, tool, ...args] of [
  ['release::test', 'npm', 'test'],
  ['hooks-install', 'node', 'scripts/install-hooks.mjs']
]) {
  test(`${recipe} uses root tooling without installer dependencies`, (t) => {
    const f = fixture(t);
    succeeded(f.run([recipe]));
    const calls = f.calls();
    assert.ok(
      calls.every((entry) => entry.cwd === f.dir),
      JSON.stringify(calls)
    );
    assert.ok(hasCall(calls, call(f.dir, null, tool, ...args)));
  });
}

test('installer::check-fast covers the commit subset without a Rust build', (t) => {
  const f = fixture(t);
  succeeded(f.run(['installer::check-fast']));
  const calls = f.calls();
  const installer = projectDir(f.dir, 'installer');
  assert.ok(
    calls.every((entry) => entry.cwd === installer),
    JSON.stringify(calls)
  );
  for (const script of ['format:check', 'lint', 'check:ts'])
    assert.ok(hasCall(calls, call(f.dir, 'installer', 'npm', 'run', script)));
  const cargo = calls.filter((entry) => entry.tool === 'cargo');
  assert.ok(cargo.length > 0);
  assert.ok(
    cargo.every((entry) => entry.args[0] === 'fmt'),
    JSON.stringify(cargo)
  );
});

function hooksFixture(t, { real = false } = {}) {
  const f = fixture(t);
  for (const file of ['install-hooks.mjs', 'git-command.mjs']) {
    fs.mkdirSync(path.join(f.dir, 'scripts'), { recursive: true });
    fs.copyFileSync(
      path.join(root, 'scripts', file),
      path.join(f.dir, 'scripts', file)
    );
  }
  // Run the installer script itself while git and lefthook can remain stubbed.
  fs.writeFileSync(
    path.join(f.dir, 'bin/node'),
    `#!/usr/bin/env bash\nexec ${shellQuote(process.execPath)} "$@"\n`
  );
  const globalDir = path.join(f.dir, 'global hooks');
  fs.mkdirSync(globalDir);
  fs.writeFileSync(path.join(globalDir, 'prepare-commit-msg'), 'untouched\n');
  const globalConfig = path.join(f.dir, 'global.gitconfig');
  fs.writeFileSync(
    globalConfig,
    `[core]\n\thooksPath = ${JSON.stringify(globalDir)}\n`
  );
  const env = {
    ...gitEnvironment(),
    PATH: `${path.join(f.dir, 'bin')}${path.delimiter}${process.env.PATH}`,
    GIT_CONFIG_NOSYSTEM: '1',
    GIT_CONFIG_GLOBAL: globalConfig
  };
  // Ignore command-scope config inherited from a developer's outer git command.
  delete env.GIT_CONFIG_COUNT;
  delete env.GIT_CONFIG_PARAMETERS;
  const commonDir = path.join(f.dir, real ? '.git' : '.git shared');
  const localConfig = path.join(commonDir, real ? 'config' : 'stub-hooks-path');
  if (real) {
    runFixtureGit(['init', '--template='], { cwd: f.dir });
    fs.symlinkSync(
      path.join(root, 'node_modules'),
      path.join(f.dir, 'node_modules'),
      'dir'
    );
    fs.copyFileSync(
      path.join(root, 'lefthook.yml'),
      path.join(f.dir, 'lefthook.yml')
    );
  } else {
    fs.mkdirSync(commonDir);
    const gitStub = path.join(f.dir, 'git-stub.mjs');
    fs.writeFileSync(
      gitStub,
      `
import fs from 'node:fs';
const args = process.argv.slice(2);
const localConfig = ${JSON.stringify(localConfig)};
fs.appendFileSync(process.env.BPP_JUST_TEST_LOG, JSON.stringify({ tool: 'git', args, cwd: process.cwd() }) + '\\n');
if (process.env.BPP_JUST_TEST_FAIL === 'git') process.exit(37);
if (args.join(' ') === 'rev-parse --path-format=absolute --git-common-dir') {
  console.log(${JSON.stringify(commonDir)});
} else if (args.join(' ') === 'config --local --get core.hooksPath') {
  if (!fs.existsSync(localConfig)) process.exit(1);
  console.log(fs.readFileSync(localConfig, 'utf8'));
} else if (args.slice(0, 4).join(' ') === 'config --local --replace-all core.hooksPath') {
  fs.writeFileSync(localConfig, args[4]);
} else if (args.join(' ') === 'rev-parse --path-format=absolute --git-path hooks') {
  console.log(process.env.BPP_JUST_TEST_HOOKS_PATH || fs.readFileSync(localConfig, 'utf8'));
} else {
  throw new Error('Unexpected git arguments: ' + JSON.stringify(args));
}
`
    );
    // Native Node cannot exec an extensionless Bash stub on Windows. Stub the
    // git-command module boundary with a Node child; real fixtures retain the
    // production module and exercise the actual Git executable.
    fs.renameSync(
      path.join(f.dir, 'scripts/git-command.mjs'),
      path.join(f.dir, 'scripts/git-command.real.mjs')
    );
    fs.writeFileSync(
      path.join(f.dir, 'scripts/git-command.mjs'),
      `
import { execFileSync } from 'node:child_process';
export { gitEnvironment } from './git-command.real.mjs';
export function runGit(args, options) {
  return execFileSync(process.execPath, [${JSON.stringify(gitStub)}, ...args], {
    encoding: 'utf8',
    ...options
  });
}
`
    );
    const lefthook = path.join(f.dir, 'node_modules/lefthook/bin/index.js');
    fs.mkdirSync(path.dirname(lefthook), { recursive: true });
    fs.writeFileSync(
      lefthook,
      `
const { spawnSync } = require('node:child_process');
const result = spawnSync(${JSON.stringify(process.execPath)}, [${JSON.stringify(path.join(f.dir, 'record.mjs'))}, 'lefthook', ...process.argv.slice(2)], { stdio: 'inherit' });
process.exit(result.status);
`
    );
  }
  return {
    ...f,
    commonDir,
    localConfig,
    globalDir,
    globalConfig,
    hooksDir: path.join(commonDir, 'hooks'),
    install(options = {}) {
      // spawnSync replaces the environment instead of reintroducing scrubbed vars.
      return spawnSync(just, ['--color', 'never', 'hooks-install'], {
        cwd: options.cwd ?? f.dir,
        encoding: 'utf8',
        timeout: 30_000,
        env: {
          ...env,
          BPP_JUST_TEST_LOG: path.join(f.dir, 'calls.jsonl'),
          ...options.env
        }
      });
    }
  };
}

test('hooks-install sets only local config and forces the root lefthook after checking its path', (t) => {
  const f = hooksFixture(t);
  succeeded(f.install({ cwd: path.join(f.dir, 'bazaarplusplus-mod') }));
  assert.equal(fs.readFileSync(f.localConfig, 'utf8'), f.hooksDir);
  assert.deepEqual(f.calls(), [
    call(
      f.dir,
      null,
      'git',
      'rev-parse',
      '--path-format=absolute',
      '--git-common-dir'
    ),
    call(f.dir, null, 'git', 'config', '--local', '--get', 'core.hooksPath'),
    call(
      f.dir,
      null,
      'git',
      'config',
      '--local',
      '--replace-all',
      'core.hooksPath',
      f.hooksDir
    ),
    call(
      f.dir,
      null,
      'git',
      'rev-parse',
      '--path-format=absolute',
      '--git-path',
      'hooks'
    ),
    call(f.dir, null, 'lefthook', 'install', '--force')
  ]);
  succeeded(f.install());
  assert.equal(
    f.calls().filter(({ args }) => args.includes('--replace-all')).length,
    1
  );
});

for (const dangling of [false, true]) {
  test(`hooks-install rejects an escaping ${dangling ? 'dangling ' : ''}symlink before writing config`, (t) => {
    const f = hooksFixture(t);
    const outside = `${f.commonDir}-outside`;
    if (!dangling) fs.mkdirSync(outside);
    fs.symlinkSync(outside, f.hooksDir, 'dir');
    assert.notEqual(f.install().status, 0);
    assert.equal(fs.existsSync(f.localConfig), false);
    assert.equal(f.calls().length, 1);
    if (!dangling) assert.deepEqual(fs.readdirSync(outside), []);
  });
}

test('hooks-install rejects an effective path outside the git directory before lefthook runs', (t) => {
  const f = hooksFixture(t);
  const result = f.install({ env: { BPP_JUST_TEST_HOOKS_PATH: f.globalDir } });
  assert.notEqual(result.status, 0);
  assert.match(
    result.stderr,
    /Refusing hooks path outside repository git directory/
  );
  assert.equal(
    f.calls().some(({ tool }) => tool === 'lefthook'),
    false
  );
});

test('hooks-install stops on git failure and preserves its exit code', (t) => {
  const f = hooksFixture(t);
  assert.equal(f.install({ env: { BPP_JUST_TEST_FAIL: 'git' } }).status, 37);
  assert.equal(fs.existsSync(f.localConfig), false);
  assert.equal(f.calls().length, 1);
});

function directoryContents(dir) {
  return Object.fromEntries(
    fs
      .readdirSync(dir)
      .sort()
      .map((name) => [name, fs.readFileSync(path.join(dir, name), 'utf8')])
  );
}

test('hooks-install in a real temporary repo overrides fake global hooks and is idempotent', (t) => {
  const f = hooksFixture(t, { real: true });
  const globalBefore = directoryContents(f.globalDir);
  const globalConfigBefore = fs.readFileSync(f.globalConfig, 'utf8');
  const globalMtimes = [
    f.globalDir,
    path.join(f.globalDir, 'prepare-commit-msg'),
    f.globalConfig
  ].map((file) => fs.statSync(file).mtimeMs);
  succeeded(f.install());
  assert.equal(
    runFixtureGit(['config', '--local', '--get', 'core.hooksPath'], {
      cwd: f.dir
    }).trim(),
    f.hooksDir
  );
  const hooks = directoryContents(f.hooksDir);
  assert.match(hooks['pre-commit'], /lefthook/);
  assert.match(hooks['pre-push'], /lefthook/);
  const localBefore = fs.readFileSync(f.localConfig, 'utf8');
  const configMtime = fs.statSync(f.localConfig).mtimeMs;
  succeeded(f.install());
  assert.deepEqual(directoryContents(f.hooksDir), hooks);
  assert.equal(fs.readFileSync(f.localConfig, 'utf8'), localBefore);
  assert.equal(fs.statSync(f.localConfig).mtimeMs, configMtime);
  assert.deepEqual(directoryContents(f.globalDir), globalBefore);
  assert.equal(fs.readFileSync(f.globalConfig, 'utf8'), globalConfigBefore);
  assert.deepEqual(
    [
      f.globalDir,
      path.join(f.globalDir, 'prepare-commit-msg'),
      f.globalConfig
    ].map((file) => fs.statSync(file).mtimeMs),
    globalMtimes
  );
});

test('hooks-install refuses a real command-scope override without writing hooks', (t) => {
  const f = hooksFixture(t, { real: true });
  const globalBefore = directoryContents(f.globalDir);
  const result = f.install({
    env: {
      GIT_CONFIG_COUNT: '1',
      GIT_CONFIG_KEY_0: 'core.hooksPath',
      GIT_CONFIG_VALUE_0: f.globalDir
    }
  });
  assert.notEqual(result.status, 0);
  assert.match(
    result.stderr,
    /Refusing hooks path outside repository git directory/
  );
  assert.equal(fs.existsSync(f.hooksDir), false);
  assert.deepEqual(directoryContents(f.globalDir), globalBefore);
});

test('hooks-install from a linked worktree uses the shared git hooks directory', (t) => {
  const f = hooksFixture(t, { real: true });
  runFixtureGit(
    [
      '-c',
      'user.name=Hooks Test',
      '-c',
      'user.email=hooks@example.test',
      'commit',
      '--allow-empty',
      '-m',
      'Initial'
    ],
    { cwd: f.dir }
  );
  const worktree = path.join(f.dir, 'linked worktree');
  runFixtureGit(['worktree', 'add', '--detach', worktree], { cwd: f.dir });
  for (const file of [
    'JUSTFILE',
    ...moduleFiles,
    'scripts/install-hooks.mjs',
    'scripts/git-command.mjs',
    'lefthook.yml'
  ]) {
    fs.mkdirSync(path.dirname(path.join(worktree, file)), { recursive: true });
    fs.copyFileSync(path.join(f.dir, file), path.join(worktree, file));
  }
  fs.symlinkSync(
    path.join(root, 'node_modules'),
    path.join(worktree, 'node_modules'),
    'dir'
  );
  succeeded(f.install({ cwd: worktree }));
  assert.equal(
    runFixtureGit(['config', '--local', '--get', 'core.hooksPath'], {
      cwd: worktree
    }).trim(),
    f.hooksDir
  );
  assert.match(
    fs.readFileSync(path.join(f.hooksDir, 'pre-commit'), 'utf8'),
    /lefthook/
  );
  assert.equal(
    fs.existsSync(
      path.join(f.commonDir, 'worktrees', path.basename(worktree), 'hooks')
    ),
    false
  );
});
