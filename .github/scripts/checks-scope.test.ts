import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'vite-plus/test';
import { runFixtureGit } from '../../scripts/test-support/git-fixture.ts';
import {
  classifyChanges,
  manualScope,
  planRun as planChecks,
  readChanges,
  scanDependencies,
  SCOPES,
  unregisteredReferences
} from './checks-scope.ts';
import type { ChecksPlan, PlanRunOptions } from './checks-scope.ts';

const planRun = (args: PlanRunOptions) => planChecks(args).plan;
const changed = (...files: string[]) =>
  files.map((file) => ({ status: 'M', file }));
const selected = (plan: ChecksPlan) =>
  Object.keys(plan)
    .filter((key) => plan[key].length)
    .sort();
const nativeInstaller = [
  'installer-frontend',
  'installer-macos',
  'installer-windows'
];
const expand = (scopes: string[]) =>
  scopes
    .flatMap((scope) =>
      scope === 'installer'
        ? ['installer-frontend', 'installer-macos']
        : scope === 'mod'
          ? ['mod-macos', 'mod-android']
          : [scope]
    )
    .sort();
const expectScopes = (files: string[], scopes: string[]) =>
  assert.deepEqual(
    selected(classifyChanges(changed(...files))),
    expand(scopes)
  );

test('root documents, each project and global inputs select their daily gates', () => {
  expectScopes(
    ['packages/runs/decisions.ts'],
    ['release', 'installer-frontend', 'site']
  );
  expectScopes(['README.md', 'AGENTS.md', 'docs/development.md'], ['release']);
  expectScopes(['bazaarplusplus-server/bun.lock'], ['server']);
  expectScopes(['bazaarplusplus-bot/src/consumer.ts'], ['bot']);
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
    ['bazaarplusplus-installer/rust-toolchain.toml'],
    ['installer', 'installer-windows']
  );
  expectScopes(
    ['bazaarplusplus-installer/src-tauri/.cargo/config.toml'],
    ['installer', 'installer-windows']
  );
  expectScopes(
    ['bazaarplusplus-installer/package.json'],
    ['installer', 'installer-windows', 'release']
  );
  expectScopes(['bazaarplusplus-mod/docs/MEMORY.md'], ['mod-macos']);
  expectScopes(
    ['bazaarplusplus-mod/src/BazaarPlusPlus.Mobile/Ui/ScrollTracker.cs'],
    ['mod']
  );
  expectScopes(['bazaarplusplus-analyzer/src/main.py'], ['analyzer']);
  expectScopes(['bazaarplusplus-site/src/main.tsx'], ['site']);
  for (const file of [
    'VERSION',
    'bun.lock',
    'JUSTFILE',
    'bunfig.toml',
    '.github/workflows/checks.yml',
    '.github/scripts/checks-scope.ts',
    'scripts/git-command.ts',
    'release/unknown-future-module.ts',
    'scripts/unknown-future-script.ts',
    'new-input'
  ])
    expectScopes([file], [...SCOPES]);
});

test('shared inputs select owners and every contract consumer', () => {
  for (const [file, consumers] of [
    [
      'bazaarplusplus-server/contracts/v6/fixtures/run-only.bundle.b64',
      ['server', 'mod', 'analyzer', 'bot']
    ],
    [
      'bazaarplusplus-server/contracts/mod-api-errors.json',
      ['server', 'mod', 'analyzer', 'bot']
    ],
    ['bazaarplusplus-server/docs/api-reference.md', ['server', 'mod', 'bot']],
    [
      'bazaarplusplus-analyzer/contracts/v6/hero-aliases.json',
      ['analyzer', 'mod', 'site', 'installer', 'bot']
    ],
    [
      'bazaarplusplus-analyzer/docs/specs/consumer-data-contract.md',
      ['analyzer', 'mod', 'site', 'installer', 'bot']
    ],
    [
      'bazaarplusplus-mod/docs/contracts/run-payload-v6.md',
      ['mod-macos', 'server', 'analyzer', 'bot', 'site']
    ],
    [
      'bazaarplusplus-mod/tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64',
      ['mod', 'server', 'analyzer', 'bot', 'site']
    ],
    [
      'bazaarplusplus-mod/tests/BundleQueueSqliteStore.Tests/fixtures/bundle-seal-eligibility.json',
      ['mod', 'installer']
    ],
    [
      'bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/local-stream-command.json',
      ['mod', 'installer']
    ],
    [
      'bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/battle-snapshots.golden.txt',
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
      ['mod', 'installer', 'release', 'android']
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
      'bazaarplusplus-installer/static/support/wechat-pay.svg',
      ['installer-frontend', 'site']
    ],
    [
      'bazaarplusplus-installer/scripts/headless.ts',
      ['installer', 'installer-windows', 'mod']
    ],
    [
      'bazaarplusplus-installer/src-tauri/icons/source/AppIcon.icon/icon.json',
      ['installer', 'installer-windows', 'macos-icon']
    ],
    [
      'bazaarplusplus-installer/scripts/release/compile-macos-icon.ts',
      ['installer', 'installer-windows', 'macos-icon']
    ]
  ] satisfies [string, string[]][])
    expectScopes([file], consumers);
});

test('deletions and cross-directory moves include old and new consumers', () => {
  assert.deepEqual(
    selected(
      classifyChanges([
        {
          status: 'D',
          file: 'bazaarplusplus-installer/static/support/wechat-pay.svg'
        },
        { status: 'A', file: 'bazaarplusplus-server/wechat-pay.svg' }
      ])
    ),
    ['installer-frontend', 'server', 'site']
  );
  assert.deepEqual(
    selected(classifyChanges([{ status: 'D', file: 'docs/design.md' }])),
    ['installer-frontend', 'mod-macos', 'release']
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
  t.onTestFinished(() => fs.rmSync(cwd, { force: true, recursive: true }));
  const git = (...args: string[]) => runFixtureGit(args, { cwd }).trim();
  const put = (file: string, text: string) => {
    fs.mkdirSync(path.dirname(path.join(cwd, file)), { recursive: true });
    fs.writeFileSync(path.join(cwd, file), text);
  };
  const commit = (message: string) => {
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
  t.onTestFinished(() => {
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
      "import '../../bazaarplusplus-installer/static/support/wechat-pay.svg'"
    ),
    []
  );
});

test('design system inputs select only the scopes that consume each design surface', () => {
  expectScopes(
    ['design/tokens.json', 'design/build.ts'],
    [
      'ui',
      'installer-frontend',
      'site',
      'ui-docs',
      'android',
      'bot',
      'server',
      'release'
    ]
  );
  assert.deepEqual(selected(classifyChanges(changed('design/design.just'))), [
    'android',
    'bot',
    'installer-frontend',
    'release',
    'server',
    'site',
    'ui',
    'ui-docs'
  ]);
  expectScopes(
    ['design/README.md', 'design/components/CardDetail/README.md'],
    ['release']
  );
  assert.deepEqual(
    selected(
      classifyChanges([
        { status: 'D', file: 'design/components/InGameKit/README.md' }
      ])
    ),
    ['installer-frontend', 'mod-macos', 'release']
  );
  expectScopes(
    ['design/components/InGameKit/art/material.png', 'design/kit-art.ts'],
    ['mod', 'release']
  );
  expectScopes(
    [
      'design/launcher-icon.ts',
      'design/assets/Covers/the-bazaar/library_hero.jpg'
    ],
    ['android', 'release']
  );
});

test('Android App inputs select only the android lane, while non-contract project docs select release', () => {
  expectScopes(
    ['bazaarplusplus-android/app/src/main/AndroidManifest.xml'],
    ['android']
  );
  expectScopes(
    [
      'bazaarplusplus-android/README.md',
      'bazaarplusplus-site/README.md',
      'bazaarplusplus-ui-docs/AGENTS.md',
      'bazaarplusplus-server/README.md',
      'bazaarplusplus-bot/README.md',
      'bazaarplusplus-analyzer/docs/architecture.md'
    ],
    ['release']
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

test('release, workspace scripts and agent configs select only their real consumers', () => {
  for (const file of [
    'release/card-art.ts',
    'release/android-release.ts',
    'release/mirror.ts',
    'release/publish.ts',
    'release/payload.test.ts',
    'scripts/workspace.ts',
    'scripts/dev/history-sample.ts',
    'scripts/qqbot/hero-report.ts',
    'scripts/android/setup-device.ts',
    'scripts/just.test.ts',
    '.agents/setup',
    '.claude/hooks/session-start.sh',
    '.github/scripts/codeql-scope.ts',
    '.github/scripts/checks-scope.test.ts'
  ])
    expectScopes([file], ['release']);

  expectScopes(
    ['release/payload.json'],
    ['release', 'mod', 'mod-windows', 'installer', 'installer-windows']
  );
  expectScopes(
    ['release/downloads.ts'],
    ['release', 'bot', 'site', 'installer', 'installer-windows', 'mod']
  );
  expectScopes(
    ['release/manifest.ts'],
    ['release', 'android', 'installer', 'installer-windows']
  );
  expectScopes(
    ['release/fixtures/latest/android-aarch64.json'],
    ['release', 'android', 'mod', 'site']
  );
  expectScopes(
    ['release/fixtures/managed-payload/manifest.txt'],
    ['release', 'android', 'installer', 'installer-windows', 'mod']
  );
  expectScopes(['release/game-libs.ts'], ['release', 'mod']);
  expectScopes(
    ['release/projections.ts'],
    ['release', 'installer', 'installer-windows']
  );
  expectScopes(
    ['scripts/test-support/git-fixture.ts'],
    ['release', 'installer', 'installer-windows']
  );
  expectScopes(['scripts/verify.ts'], ['release', 'mod', 'mod-windows']);
});

test('every executable or data reference from projects into release/, scripts/ and design/ (including transitive TS imports) is covered', () => {
  const root = path.resolve(import.meta.dirname, '../..');
  const tracked = runFixtureGit(['ls-files', '-z'], { cwd: root })
    .split('\0')
    .filter((file) => file && fs.existsSync(path.join(root, file)));
  const consumerScopesFor = (file: string): string[] => {
    if (file.startsWith('packages/ui/')) return ['ui'];
    if (file.startsWith('packages/runs/')) return ['release'];
    const match = file.match(
      /^bazaarplusplus-(site|ui-docs|server|bot|analyzer|installer|mod|android)\//
    );
    if (!match) return [];
    const project = match[1];
    // Dev/local-only just recipes (dev, cli, stage-runtime, etc.) are not run in project CI.
    if (file.endsWith('.just')) return [];
    if (project === 'installer') {
      return file.startsWith('bazaarplusplus-installer/src/') &&
        !file.startsWith('bazaarplusplus-installer/src/types/generated/')
        ? ['installer-frontend']
        : ['installer-frontend', 'installer-macos'];
    }
    if (project === 'mod') return ['mod-macos', 'mod-android'];
    return [project];
  };

  const TS_IMPORT =
    /^\s*(?:import|export)\s+(?!type\s)[^;'"]*?\bfrom\s+['"](\.[^'"]+)['"]|^\s*import\s+['"](\.[^'"]+)['"]|\bimport\(\s*['"](\.[^'"]+)['"]\s*\)/gm;
  const RELATIVE_ROOT_REF = /(?:\.\.\/)+(?:release|scripts|design)\/[\w./-]+/g;
  const resolveRelative = (
    fromFile: string,
    specifier: string
  ): string | null => {
    const projectRoot = fromFile.match(/^bazaarplusplus-[^/]+/)?.[0];
    const bases = [path.posix.dirname(fromFile)];
    if (projectRoot && projectRoot !== bases[0]) bases.push(projectRoot);
    for (const dir of bases) {
      const base = path.posix.normalize(path.posix.join(dir, specifier));
      for (const candidate of [base, `${base}.ts`, `${base}.tsx`]) {
        if (fs.existsSync(path.join(root, candidate))) return candidate;
      }
    }
    return null;
  };
  const isRootShared = (target: string) =>
    target.startsWith('release/') ||
    target.startsWith('scripts/') ||
    target.startsWith('design/');

  const expandTarget = (entry: string, followTsImports: boolean): string[] => {
    const out: string[] = [];
    const queue = [entry];
    const seen = new Set<string>();
    while (queue.length) {
      const current = queue.pop()!;
      if (seen.has(current)) continue;
      seen.add(current);
      if (!fs.existsSync(path.join(root, current))) continue;
      const stat = fs.statSync(path.join(root, current));
      if (stat.isDirectory()) {
        for (const child of tracked) {
          if (child.startsWith(`${current}/`)) out.push(child);
        }
        continue;
      }
      out.push(current);
      if (followTsImports && current.endsWith('.ts')) {
        const source = fs.readFileSync(path.join(root, current), 'utf8');
        for (const match of source.matchAll(TS_IMPORT)) {
          const spec = match[1] ?? match[2] ?? match[3];
          if (!spec) continue;
          const next = resolveRelative(current, spec);
          if (next && isRootShared(next)) queue.push(next);
        }
      }
    }
    return out;
  };

  const missing: string[] = [];
  let checkedTargets = 0;
  for (const file of tracked) {
    if (file.endsWith('.md')) continue;
    const requiredScopes = consumerScopesFor(file);
    if (!requiredScopes.length) continue;
    const source = fs.readFileSync(path.join(root, file), 'utf8');
    if (source.includes('\0')) continue;
    const isTsLike = /\.(?:[cm]?[jt]sx?)$/.test(file);
    const directTargets: { target: string; followTs: boolean }[] = [];
    if (isTsLike) {
      for (const match of source.matchAll(TS_IMPORT)) {
        const spec = match[1] ?? match[2] ?? match[3];
        if (!spec) continue;
        const resolved = resolveRelative(file, spec);
        if (resolved && isRootShared(resolved))
          directTargets.push({ target: resolved, followTs: true });
      }
    }
    for (const [rawRef] of source
      .replaceAll('\\', '/')
      .matchAll(RELATIVE_ROOT_REF)) {
      const resolved = resolveRelative(file, rawRef.replace(/[/.]+$/, ''));
      if (resolved && isRootShared(resolved))
        directTargets.push({ target: resolved, followTs: false });
    }
    for (const { target, followTs } of directTargets) {
      for (const expanded of expandTarget(target, followTs)) {
        checkedTargets += 1;
        const lanes = selected(classifyChanges(changed(expanded)));
        for (const scope of requiredScopes) {
          if (!lanes.includes(scope)) {
            missing.push(`${file} -> ${expanded} (missing ${scope})`);
          }
        }
      }
    }
  }
  assert.ok(
    checkedTargets > 30,
    `expected >30 checked targets, got ${checkedTargets}`
  );
  assert.deepEqual([...new Set(missing)].sort(), []);
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
    'scripts/checks/verify.ts',
    'scripts/generate-bindings.ts',
    'src/types/generated/commands.ts',
    'rust-toolchain.toml',
    'unknown-input'
  ]) {
    expectScopes(
      [`bazaarplusplus-installer/${file}`],
      ['installer', 'installer-windows']
    );
  }
  for (const file of ['package.json', 'bunfig.toml']) {
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
  const expected: Record<string, string[]> = {
    'full/all': [...SCOPES].sort(),
    'full/macos': [
      'analyzer',
      'bot',
      'installer-frontend',
      'installer-macos',
      'macos-icon',
      'mod-macos',
      'release',
      'server',
      'site',
      'ui',
      'ui-docs'
    ],
    'full/windows': [
      'analyzer',
      'bot',
      'installer-frontend',
      'installer-windows',
      'mod-windows',
      'release',
      'server',
      'site',
      'ui',
      'ui-docs'
    ],
    'full/android': [
      'analyzer',
      'android',
      'bot',
      'installer-frontend',
      'mod-android',
      'release',
      'server',
      'site',
      'ui',
      'ui-docs'
    ],
    'installer/all': [
      'installer-frontend',
      'installer-macos',
      'installer-windows',
      'macos-icon'
    ],
    'installer/macos': ['installer-frontend', 'installer-macos', 'macos-icon'],
    'installer/windows': ['installer-frontend', 'installer-windows'],
    'installer/android': ['installer-frontend'],
    'mod/all': ['mod-android', 'mod-macos', 'mod-windows'],
    'mod/macos': ['mod-macos'],
    'mod/windows': ['mod-windows'],
    'mod/android': ['mod-android']
  };
  for (const [selection, lanes] of Object.entries(expected)) {
    const [scope, platform] = selection.split('/');
    assert.deepEqual(
      selected(
        planRun({
          eventName: 'workflow_dispatch',
          event: { inputs: { scope, platform } }
        })
      ),
      lanes,
      selection
    );
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
    ['mod', 'mod-windows', 'release', 'site']
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

test('desktop host C# selects macOS while shared and Android inputs retain both hosts', () => {
  const mod = 'bazaarplusplus-mod/';
  expectScopes([`${mod}src/BazaarPlusPlus/Game/Panel.cs`], ['mod-macos']);
  expectScopes(
    [`${mod}src/BazaarPlusPlus/Game/WindowsPanel.cs`],
    ['mod-macos', 'mod-windows']
  );
  for (const file of [
    'src/BazaarPlusPlus.Core/Game/Shared.cs',
    'src/BazaarPlusPlus.Mobile/Ui/Panel.cs',
    'src/BazaarPlusPlus.Android/Host/AndroidMod.cs',
    'tests/Architecture.Tests/LogEventIdRule.cs',
    'tests/AndroidHost.Tests/AndroidLaunchTests.cs',
    'src/BazaarPlusPlus/Resources/HistoryPanel/history-menu.png',
    'src/BazaarPlusPlus/Data/BuildRecommendations/builds.json'
  ])
    expectScopes([`${mod}${file}`], ['mod']);
  expectScopes(
    [
      `${mod}src/BazaarPlusPlus/Game/Panel.cs`,
      `${mod}src/BazaarPlusPlus.Core/Game/Shared.cs`
    ],
    ['mod']
  );
  expectScopes(
    [
      `${mod}src/BazaarPlusPlus/Game/Panel.cs`,
      `${mod}tests/Architecture.Tests/LogEventIdRule.cs`
    ],
    ['mod']
  );
  assert.deepEqual(
    selected(
      classifyChanges([
        { status: 'D', file: `${mod}src/BazaarPlusPlus/Game/Panel.cs` },
        { status: 'A', file: `${mod}src/BazaarPlusPlus.Core/Game/Panel.cs` }
      ])
    ),
    ['mod-android', 'mod-macos']
  );
});

test('repository metadata and project recipes do not fan out to all platforms', () => {
  for (const file of [
    '.gitignore',
    '.gitattributes',
    '.github/dependabot.yml',
    '.github/workflows/codeql.yml',
    '.github/codeql/codeql-config.yml'
  ])
    expectScopes([file], ['release']);
  expectScopes(['bazaarplusplus-ui-docs/src/main.tsx'], ['ui-docs']);
  expectScopes(
    ['packages/ui/src/Button.tsx'],
    ['ui', 'installer-frontend', 'site', 'ui-docs']
  );
  expectScopes(
    ['packages/ui/package.json'],
    ['ui', 'installer-frontend', 'site', 'ui-docs', 'release']
  );
  expectScopes(['bazaarplusplus-site/site.just'], ['site', 'release']);
  expectScopes(['bazaarplusplus-site/package.json'], ['site', 'release']);
  expectScopes(['bazaarplusplus-site/src/windows.ts'], ['site']);
});

test('compatibility builds run for snapshot updates and manual checks, not daily source changes', () => {
  const automatic = (file: string) =>
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
  ] satisfies [string, string, boolean][]) {
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
