import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { runFixtureGit } from '../../scripts/test-support/git-fixture.mjs';
import {
  classifyChanges,
  manualScope,
  planRun as planChecks,
  readChanges,
  scanDependencies,
  SCOPES,
  unregisteredReferences
} from './checks-scope.mjs';

const planRun = (args) => planChecks(args).plan;
const changed = (...files) => files.map((file) => ({ status: 'M', file }));
const selected = (plan) =>
  Object.keys(plan)
    .filter((key) => plan[key].length)
    .sort();
const nativeInstaller = [
  'installer-frontend',
  'installer-macos',
  'installer-windows'
];
const expand = (scopes) =>
  scopes
    .flatMap((scope) =>
      scope === 'installer'
        ? ['installer-frontend', 'installer-macos']
        : scope === 'mod'
          ? ['mod-macos', 'mod-android']
          : [scope]
    )
    .sort();
const expectScopes = (files, scopes) =>
  assert.deepEqual(
    selected(classifyChanges(changed(...files))),
    expand(scopes)
  );

test('root documents, each project and global inputs select their daily gates', () => {
  expectScopes(['README.md', 'AGENTS.md', 'docs/development.md'], ['release']);
  expectScopes(['bazaarplusplus-server/package-lock.json'], ['server']);
  expectScopes(
    ['bazaarplusplus-installer/src/App.tsx'],
    ['installer-frontend']
  );
  expectScopes(
    ['bazaarplusplus-installer/src-tauri/src/lib.rs'],
    ['installer']
  );
  expectScopes(
    ['bazaarplusplus-installer/src/lib/bindings.ts'],
    ['installer-frontend']
  );
  expectScopes(
    ['bazaarplusplus-installer/src-tauri/src/v5_import/mod.rs'],
    ['installer', 'release']
  );
  expectScopes(
    ['bazaarplusplus-installer/rust-toolchain.toml'],
    ['installer', 'installer-windows']
  );
  expectScopes(
    ['bazaarplusplus-installer/src-tauri/.cargo/config.toml'],
    ['installer', 'installer-windows']
  );
  expectScopes(
    ['bazaarplusplus-installer/package-lock.json'],
    ['installer', 'installer-windows', 'release']
  );
  expectScopes(['bazaarplusplus-mod/docs/MEMORY.md'], ['mod-macos']);
  // The Mobile UI tree compiles into both hosts (mod ADR-0025), so it builds both.
  expectScopes(
    ['bazaarplusplus-mod/src/BazaarPlusPlus.Mobile/Ui/ScrollTracker.cs'],
    ['mod']
  );
  expectScopes(['bazaarplusplus-analyzer/src/main.py'], ['analyzer']);
  expectScopes(['bazaarplusplus-site/src/main.tsx'], ['site']);
  for (const file of [
    'VERSION',
    'release/payload.json',
    'release/manifest.mjs',
    'package-lock.json',
    'JUSTFILE',
    '.nvmrc',
    '.github/workflows/checks.yml',
    'scripts/workspace.mjs',
    'new-input'
  ])
    expectScopes([file], [...SCOPES]);
});

test('shared inputs select owners and every contract consumer', () => {
  for (const [file, consumers] of [
    [
      'bazaarplusplus-server/contracts/v6/fixtures/run-only.bundle.b64',
      ['server', 'mod', 'analyzer']
    ],
    [
      'bazaarplusplus-server/contracts/mod-api-errors.json',
      ['server', 'mod', 'analyzer']
    ],
    ['bazaarplusplus-server/docs/api-reference.md', ['server', 'mod']],
    [
      'bazaarplusplus-analyzer/contracts/v6/hero-aliases.json',
      ['analyzer', 'mod', 'site', 'installer']
    ],
    [
      'bazaarplusplus-analyzer/docs/specs/consumer-data-contract.md',
      ['analyzer', 'mod', 'site', 'installer']
    ],
    [
      'bazaarplusplus-mod/docs/contracts/run-payload-v6.md',
      ['mod-macos', 'server', 'analyzer']
    ],
    [
      'bazaarplusplus-mod/tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64',
      ['mod', 'server', 'analyzer']
    ],
    [
      'bazaarplusplus-mod/tests/BundleQueueSqliteStore.Tests/fixtures/bundle-seal-eligibility.json',
      ['mod', 'installer']
    ],
    [
      'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs',
      ['mod', 'installer']
    ],
    [
      'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs',
      ['mod', 'installer', 'release']
    ],
    [
      'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json',
      ['mod', 'installer', 'release']
    ],
    [
      'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/history-database-v6.schema.sql',
      ['mod', 'installer', 'release']
    ],
    [
      'bazaarplusplus-installer/src-tauri/history-database-compatibility.json',
      ['installer', 'installer-windows', 'mod', 'release']
    ],
    [
      'bazaarplusplus-installer/src/styles/tokens.css',
      ['installer-frontend', 'site']
    ],
    [
      'bazaarplusplus-installer/static/support/wechat-pay.svg',
      ['installer-frontend', 'site']
    ],
    [
      'bazaarplusplus-installer/scripts/headless.mjs',
      ['installer', 'installer-windows', 'mod']
    ],
    [
      'bazaarplusplus-installer/src-tauri/icons/source/AppIcon.icon/icon.json',
      ['installer', 'installer-windows', 'macos-icon']
    ],
    [
      'bazaarplusplus-installer/scripts/release/compile-macos-icon.mjs',
      ['installer', 'installer-windows', 'macos-icon']
    ]
  ])
    expectScopes([file], consumers);
});

test('deletions and cross-directory moves include old and new consumers', () => {
  assert.deepEqual(
    selected(
      classifyChanges([
        { status: 'D', file: 'bazaarplusplus-installer/src/styles/tokens.css' },
        { status: 'A', file: 'bazaarplusplus-server/tokens.css' }
      ])
    ),
    ['installer-frontend', 'server', 'site']
  );
  assert.deepEqual(
    selected(classifyChanges([{ status: 'D', file: 'docs/design.md' }])),
    [...SCOPES].sort()
  );
  for (const changes of [
    null,
    [],
    [{ status: 'R100', file: 'bad' }],
    [{ status: 'M', file: '../escape' }]
  ])
    assert.deepEqual(selected(classifyChanges(changes)), [...SCOPES].sort());
});

test('unavailable diffs, zero push base, manual and unknown events fail open to full coverage', () => {
  for (const args of [
    {
      eventName: 'push',
      event: { before: '0'.repeat(40), after: 'b'.repeat(40) }
    },
    {
      eventName: 'push',
      event: { before: 'a'.repeat(40), after: 'b'.repeat(40) },
      runGit: () => {
        throw new Error('shallow history');
      }
    },
    { eventName: 'pull_request', event: {} },
    { eventName: 'workflow_dispatch', event: {} },
    { eventName: 'unknown', event: {} },
    { dependencyProblems: ['new cross-project read'], event: {} }
  ])
    assert.deepEqual(selected(planRun(args)), [...SCOPES].sort());
});

test('real Git merge-base ignores base-only changes; push uses the whole interval, including moves and unusual names', (t) => {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-checks-diff-'));
  t.after(() => fs.rmSync(cwd, { force: true, recursive: true }));
  const git = (...args) => runFixtureGit(args, { cwd }).trim();
  const put = (file, text) => {
    fs.mkdirSync(path.dirname(path.join(cwd, file)), { recursive: true });
    fs.writeFileSync(path.join(cwd, file), text);
  };
  const commit = (message) => {
    git('add', '.');
    git(
      '-c',
      'user.name=Fixture',
      '-c',
      'user.email=fixture@example.test',
      'commit',
      '-qm',
      message
    );
    return git('rev-parse', 'HEAD');
  };
  git('init');
  put('bazaarplusplus-server/old name\n.txt', 'value');
  const before = commit('base');
  git('checkout', '-b', 'feature');
  fs.rmSync(path.join(cwd, 'bazaarplusplus-server/old name\n.txt'));
  put('bazaarplusplus-site/moved.txt', 'value');
  commit('move');
  put('bazaarplusplus-installer/src/App.tsx', 'input');
  const head = commit('second change');
  git('checkout', 'main');
  put('VERSION', 'base only');
  const base = commit('base advanced');
  // Git hooks export the caller's repository; the explicit fixture cwd must win.
  const previousGitDir = process.env.GIT_DIR;
  t.after(() => {
    if (previousGitDir === undefined) delete process.env.GIT_DIR;
    else process.env.GIT_DIR = previousGitDir;
  });
  process.env.GIT_DIR = runFixtureGit(['rev-parse', '--absolute-git-dir'], {
    cwd: process.cwd()
  }).trim();
  const actual = readChanges(
    'pull_request',
    { pull_request: { base: { sha: base }, head: { sha: head } } },
    cwd
  );
  assert.deepEqual(selected(classifyChanges(actual)), [
    'installer-frontend',
    'server',
    'site'
  ]);
  assert.ok(
    actual.some(({ status, file }) => status === 'D' && file.includes('\n'))
  );
  assert.deepEqual(readChanges('push', { before, after: head }, cwd), actual);
});

test('all executable cross-project references are registered', () => {
  assert.deepEqual(scanDependencies(), []);
});

test('new cross-project reads cannot silently bypass consumers, including backslash paths', () => {
  const file = 'bazaarplusplus-site/src/test.ts';
  for (const source of [
    "import x from '../../bazaarplusplus-server/new-contract.json'",
    "import x from '../../bazaarplusplus-installer/static/support/../../secret.json'",
    'read("../bazaarplusplus-server/secret.json")',
    'read("..\\bazaarplusplus-server\\secret.json")'
  ])
    assert.equal(unregisteredReferences(file, source).length, 1);
  assert.deepEqual(
    unregisteredReferences(
      file,
      "import '../../bazaarplusplus-installer/src/styles/tokens.css'"
    ),
    []
  );
});

test('design system inputs select the installer frontend, the site, the Android App and the root gates', () => {
  expectScopes(
    [
      'design/tokens.json',
      'design/components/bundle.css',
      'design/build.mjs',
      'design/README.md'
    ],
    ['installer-frontend', 'site', 'android', 'release']
  );
  assert.deepEqual(selected(classifyChanges(changed('design/design.just'))), [
    'android',
    'installer-frontend',
    'release',
    'site'
  ]);
});

test('Android App inputs select only the android lane', () => {
  expectScopes(
    [
      'bazaarplusplus-android/app/src/main/AndroidManifest.xml',
      'bazaarplusplus-android/README.md'
    ],
    ['android']
  );
  expectScopes(['bazaarplusplus-android/android.just'], ['android', 'release']);
  assert.deepEqual(
    unregisteredReferences(
      'bazaarplusplus-android/app/build.gradle.kts',
      'file("../bazaarplusplus-mod/build/game-libs.lock.json")'
    ),
    [
      'bazaarplusplus-android/app/build.gradle.kts -> bazaarplusplus-mod/build/game-libs.lock.json'
    ]
  );
});

test('Installer build inputs request Windows while ordinary source uses macOS', () => {
  for (const file of [
    'src/App.tsx',
    'src/styles/app.css',
    'src/test.test.ts',
    'docs/architecture.md',
    'README.md',
    'vite.config.ts',
    'vitest.config.ts',
    'tsconfig.json',
    'index.html',
    '.oxlintrc.json',
    'static/image.png'
  ]) {
    expectScopes([`bazaarplusplus-installer/${file}`], ['installer-frontend']);
  }
  for (const file of [
    'src-tauri/build.rs',
    'src-tauri/build_support/mod.rs',
    'src-tauri/capabilities/main.json',
    'src-tauri/icons/icon.png',
    '.cargo/config.toml',
    'scripts/checks/verify.mjs',
    'scripts/generate-bindings.mjs',
    'src/types/generated/commands.ts',
    'rust-toolchain.toml',
    'unknown-input'
  ]) {
    expectScopes(
      [`bazaarplusplus-installer/${file}`],
      ['installer', 'installer-windows']
    );
  }
  for (const file of ['package.json', 'package-lock.json']) {
    expectScopes(
      [`bazaarplusplus-installer/${file}`],
      ['installer', 'installer-windows', 'release']
    );
  }
  expectScopes(
    ['bazaarplusplus-installer/installer.just'],
    ['installer', 'installer-windows', 'release']
  );
  assert.deepEqual(
    selected(
      classifyChanges([
        {
          status: 'D',
          file: 'bazaarplusplus-installer/src/types/generated/commands.ts'
        },
        { status: 'A', file: 'bazaarplusplus-installer/src/commands.ts' }
      ])
    ),
    [...nativeInstaller].sort()
  );
});

test('manual scopes select exactly the requested native platforms and their source gates', () => {
  for (const scope of ['full', 'installer', 'mod']) {
    for (const platform of ['all', 'macos', 'windows', 'android']) {
      const expected =
        scope === 'full'
          ? [...SCOPES]
          : scope === 'installer'
            ? [...nativeInstaller, 'macos-icon']
            : ['mod-macos', 'mod-windows', 'mod-android'];
      const native = (lane) =>
        lane === 'macos-icon'
          ? 'macos'
          : lane === 'android'
            ? 'android'
            : ['macos', 'windows', 'android'].find((name) =>
                lane.endsWith(`-${name}`)
              );
      const filtered = expected.filter(
        (lane) =>
          platform === 'all' || !native(lane) || native(lane) === platform
      );
      assert.deepEqual(
        selected(
          planRun({
            eventName: 'workflow_dispatch',
            event: { inputs: { scope, platform } }
          })
        ),
        filtered.sort()
      );
    }
  }
  assert.deepEqual(selected(manualScope()), [...SCOPES].sort());
  for (const inputs of [
    { scope: 'site' },
    { platform: 'linux' },
    { scope: '' }
  ]) {
    assert.throws(() => manualScope(inputs), /Invalid manual selection/);
  }
});

test('manual checks fail closed when dependency scanning or history retrieval is unavailable', () => {
  for (const problem of [
    'new cross-project read',
    'Dependency scan unavailable',
    'History fetch failed'
  ]) {
    assert.throws(
      () =>
        planRun({
          eventName: 'workflow_dispatch',
          event: { inputs: { scope: 'installer', platform: 'windows' } },
          dependencyProblems: [problem]
        }),
      /Unregistered cross-project reference/
    );
  }
});

test('ordinary Mod source skips Windows; build and snapshot inputs retain it', () => {
  expectScopes(
    ['bazaarplusplus-mod/src/BazaarPlusPlus.Core/Example.cs'],
    ['mod']
  );
  for (const file of [
    'Directory.Build.props',
    'global.json',
    'src/BazaarPlusPlus/BazaarPlusPlus.csproj',
    'src/BazaarPlusPlus/packages.lock.json',
    'native/windows/build.ps1'
  ]) {
    const extra = file === 'global.json' ? ['release'] : [];
    expectScopes(
      [`bazaarplusplus-mod/${file}`],
      ['mod', 'mod-windows', ...extra]
    );
  }
  expectScopes(
    ['bazaarplusplus-mod/build/game-libs.lock.json'],
    ['mod', 'mod-windows', 'release']
  );
  expectScopes(
    ['bazaarplusplus-installer/src-tauri/src/services/history.rs'],
    ['installer']
  );
  expectScopes(
    ['bazaarplusplus-installer/src-tauri/src/windows.rs'],
    ['installer', 'installer-windows']
  );
});

test('repository metadata and project recipes do not fan out to all platforms', () => {
  for (const file of [
    '.gitignore',
    '.gitattributes',
    '.github/dependabot.yml',
    '.github/workflows/codeql.yml'
  ])
    expectScopes([file], ['release']);
  expectScopes(['bazaarplusplus-site/site.just'], ['site', 'release']);
  expectScopes(['bazaarplusplus-site/src/windows.ts'], ['site']);
});

test('compatibility builds run for snapshot updates and manual checks, not daily source changes', () => {
  const automatic = (file) =>
    planChecks({
      eventName: 'push',
      event: { before: 'a'.repeat(40), after: 'b'.repeat(40) },
      runGit: () => `M\0${file}\0`
    });
  assert.equal(
    automatic('bazaarplusplus-mod/src/BazaarPlusPlus.Core/Example.cs')
      .compatibility,
    false
  );
  const snapshot = automatic('bazaarplusplus-mod/build/game-libs.lock.json');
  assert.equal(snapshot.compatibility, true);
  assert.ok(snapshot.plan['mod-windows'].length);
  assert.ok(snapshot.plan['mod-macos'].length);
  assert.equal(
    planChecks({ eventName: 'workflow_dispatch', event: {} }).compatibility,
    true
  );
  assert.equal(
    planChecks({ eventName: 'push', event: {} }).compatibility,
    true
  );
});

test('extended checks belong only to selected desktop Mod jobs', () => {
  for (const [scope, platform, enabled] of [
    ['full', 'all', true],
    ['mod', 'macos', true],
    ['mod', 'windows', true],
    ['mod', 'android', false],
    ['installer', 'all', false]
  ]) {
    const plan = planChecks({
      eventName: 'workflow_dispatch',
      event: { inputs: { scope, platform } }
    });
    assert.equal(plan.compatibility, enabled, `${scope}/${platform}`);
    assert.equal(plan.performance, enabled, `${scope}/${platform}`);
  }
  for (const file of [
    'bazaarplusplus-mod/src/BazaarPlusPlus.Core/Example.cs',
    'bazaarplusplus-mod/build/game-libs.lock.json'
  ]) {
    const plan = planChecks({
      eventName: 'push',
      event: { before: 'a'.repeat(40), after: 'b'.repeat(40) },
      runGit: () => `M\0${file}\0`
    });
    assert.equal(plan.performance, false, file);
  }
  assert.equal(planChecks({ eventName: 'push', event: {} }).performance, false);
});
