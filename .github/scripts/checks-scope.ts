import fs from 'node:fs';
import path from 'node:path';
import { runGit } from '../../scripts/git-command.ts';

export type Project =
  | 'site'
  | 'ui-docs'
  | 'server'
  | 'bot'
  | 'analyzer'
  | 'installer'
  | 'mod'
  | 'android';

export type ChecksPlan = Record<string, string[]>;

export interface DiffChange {
  status: string;
  file: string;
}

export interface ManualInputs {
  scope?: string;
  platform?: string;
}

export interface ManualSelection {
  scope: string;
  platform: string;
}

export interface ChecksEvent {
  pull_request?: { head?: { sha?: string }; base?: { sha?: string } };
  before?: string;
  after?: string;
  inputs?: ManualInputs;
}

export interface PlanRunOptions {
  eventName?: string;
  event: ChecksEvent;
  cwd?: string;
  runGit?: (args: readonly string[]) => string;
  dependencyProblems?: string[];
}

export interface PlanRunResult {
  plan: ChecksPlan;
  compatibility: boolean;
  performance: boolean;
}

export const PROJECTS: Project[] = [
  'site',
  'ui-docs',
  'server',
  'bot',
  'analyzer',
  'installer',
  'mod',
  'android'
];
const PROJECT_SCOPES: Partial<Record<string, string[]>> = {
  installer: ['installer-frontend', 'installer-macos', 'installer-windows'],
  mod: ['mod-macos', 'mod-windows', 'mod-android']
};
export const SCOPES: string[] = [
  'release',
  'ui',
  ...PROJECTS.flatMap((project) => PROJECT_SCOPES[project] ?? [project]),
  'macos-icon'
];

// Only known frontend inputs may avoid native verification. New build inputs
// default to native; generated bindings must be checked against a real export.
export function installerFrontendOnly(file: string): boolean {
  const relative = file.slice('bazaarplusplus-installer/'.length);
  if (relative.startsWith('src/types/generated/')) return false;
  return (
    ['src/', 'static/', 'docs/'].some((prefix) =>
      relative.startsWith(prefix)
    ) ||
    /^[^/]+\.md$/.test(relative) ||
    [
      'index.html',
      'vite.config.ts',
      'vitest.config.ts',
      'tsconfig.json',
      'LICENSE'
    ].includes(relative)
  );
}

// Daily checks cover one desktop OS and both Mod hosts. Windows-specific
// runtime regressions in shared source may be found only by release validation.
function dailyScopes(project: string): string[] {
  const scopes = PROJECT_SCOPES[project];
  return scopes
    ? scopes.filter((scope) => !scope.endsWith('-windows'))
    : [project];
}

export function windowsBuildInput(project: string, file: string): boolean {
  if (!['mod', 'installer'].includes(project)) return false;
  const relative = file.slice(`bazaarplusplus-${project}/`.length);
  if (/windows/i.test(relative)) return true;
  if (project === 'mod') {
    return (
      /\.(csproj|props|targets|slnx)$/.test(relative) ||
      relative.endsWith('packages.lock.json') ||
      relative === 'global.json' ||
      relative.endsWith('.just') ||
      ['build/', 'scripts/', 'native/', '.config/'].some((prefix) =>
        relative.startsWith(prefix)
      )
    );
  }
  return (
    project === 'installer' &&
    !installerFrontendOnly(file) &&
    !['src-tauri/src/', 'src-tauri/tests/'].some((prefix) =>
      relative.startsWith(prefix)
    )
  );
}

export function manualSelection(inputs: ManualInputs = {}): ManualSelection {
  const scope = inputs.scope ?? 'full';
  const platform = inputs.platform ?? 'all';
  if (
    !['full', 'installer', 'mod'].includes(scope) ||
    !['all', 'macos', 'windows', 'android'].includes(platform)
  ) {
    throw new Error(`Invalid manual selection: ${scope}/${platform}`);
  }
  return { scope, platform };
}

export function manualScope(inputs?: ManualInputs): ChecksPlan {
  const { scope, platform } = manualSelection(inputs);
  return Object.fromEntries(
    SCOPES.map((lane) => {
      const inScope =
        scope === 'full' ||
        lane.startsWith(`${scope}-`) ||
        (scope === 'installer' && lane === 'macos-icon');
      const native =
        lane === 'macos-icon'
          ? 'macos'
          : lane === 'android'
            ? 'android'
            : ['macos', 'windows', 'android'].find((name) =>
                lane.endsWith(`-${name}`)
              );
      const inPlatform = platform === 'all' || !native || native === platform;
      return [
        lane,
        inScope && inPlatform ? [`Manual selection: ${scope}/${platform}`] : []
      ];
    })
  );
}

// AGENTS.md owns the contracts. Every classification also checks executable references.
export const SHARED_INPUTS: [string, string[]][] = [
  [
    'bazaarplusplus-analyzer/tests/fixtures/run-payload-v6.projection.json',
    ['bot']
  ],
  ['bazaarplusplus-server/contracts/', ['mod', 'analyzer', 'bot']],
  ['bazaarplusplus-server/docs/api-reference.md', ['mod', 'bot']],
  ['bazaarplusplus-analyzer/contracts/', ['site', 'mod', 'installer', 'bot']],
  [
    'bazaarplusplus-analyzer/docs/specs/consumer-data-contract.md',
    ['site', 'mod', 'installer', 'bot']
  ],
  ['bazaarplusplus-mod/docs/contracts/', ['server', 'analyzer', 'bot', 'site']],
  // Launch Record (AGENTS.md): the mod's Android Host and Updater read the
  // Android App's golden.
  ['bazaarplusplus-android/contracts/', ['mod']],
  [
    'bazaarplusplus-mod/tests/BundleV6Codec.Tests/fixtures/',
    ['server', 'analyzer', 'bot', 'site']
  ],
  [
    'bazaarplusplus-mod/tests/BundleQueueSqliteStore.Tests/fixtures/',
    ['installer']
  ],
  [
    'bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/local-stream-command.json',
    ['installer']
  ],
  // Battle snapshot JSON (AGENTS.md): installer History reads the mod's golden.
  [
    'bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/battle-snapshots.golden.txt',
    ['installer']
  ],
  [
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/history-database-v6.schema.sql',
    ['installer', 'release']
  ],
  ['bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BundleQueue/', ['installer']],
  [
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs',
    ['installer', 'release']
  ],
  [
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json',
    ['installer', 'release', 'android']
  ],
  // The site's Game Message Schema generator takes its default snapshots from the lock.
  ['bazaarplusplus-mod/build/game-libs.lock.json', ['release', 'site']],
  // Untracked snapshots that the site's Game Message Schema generator reads.
  ['bazaarplusplus-mod/game-libs/', ['site']],
  // release/snapshot-lock.test.ts pins GAME_VERSION_PATTERN to the first and
  // release/game-libs-interop.test.ts evaluates the second against a captured
  // lock.
  ['bazaarplusplus-mod/build/ManagedPath.props', ['release']],
  ['bazaarplusplus-mod/build/InteropPath.props', ['release']],
  ['bazaarplusplus-mod/global.json', ['release']],
  // The docs authoring tool type-checks against the site's decoder and fixture builder;
  // the installer's Browser Preview Run is decoded from the same fixture.
  [
    'bazaarplusplus-site/src/features/runs/run-payload.ts',
    ['installer', 'ui-docs']
  ],
  ['bazaarplusplus-site/tools/run-fixture/build.ts', ['ui-docs']],
  [
    'bazaarplusplus-site/test/fixtures/run-stream.payload.b64',
    ['installer', 'ui-docs']
  ],
  // scripts/vite-plus.test.ts pins its vite-plus version to root package.json.
  ['bazaarplusplus-ui-docs/package.json', ['release']],
  ['bazaarplusplus-site/package.json', ['release']],
  ['bazaarplusplus-installer/static/support/', ['site']],
  ['bazaarplusplus-installer/src-tauri/resources/', ['mod']],
  ['bazaarplusplus-installer/scripts/headless.ts', ['mod']],
  [
    'bazaarplusplus-installer/src-tauri/history-database-compatibility.json',
    ['mod', 'release']
  ],
  // Release signing public key (AGENTS.md): Updater.Tests pins
  // ReleaseSigningKey.PublicKeyLine to plugins.updater.pubkey.
  ['bazaarplusplus-installer/src-tauri/tauri.conf.json', ['mod']],
  ...[
    'package.json',
    'bunfig.toml',
    'src-tauri/Cargo.toml',
    'src-tauri/Cargo.lock',
    'src-tauri/tauri.conf.json',
    'src-tauri/tauri.macos.conf.json',
    'src-tauri/tauri.windows.conf.json'
  ].map((file): [string, string[]] => [
    `bazaarplusplus-installer/${file}`,
    ['release']
  ]),
  // The desktop app icon is the Android App's launcher icon too
  // (design/launcher-icon.ts exports it, and the release lane's design::check
  // fails a stale export).
  [
    'bazaarplusplus-installer/src-tauri/icons/app-icon.png',
    ['android', 'release']
  ],
  ['bazaarplusplus-installer/src-tauri/icons/source/', ['macos-icon']],
  [
    'bazaarplusplus-installer/scripts/release/compile-macos-icon.ts',
    ['macos-icon']
  ]
];

function covers(input: string, file: string): boolean {
  return file === input || (input.endsWith('/') && file.startsWith(input));
}

const INFORMATIONAL_REFERENCES: [string, string][] = [
  [
    'bazaarplusplus-server/test/contracts/ghost-summary-contract.test.ts',
    'bazaarplusplus-mod/tests/ModApi.Tests/GhostSummaryContractTests.cs'
  ],
  [
    'bazaarplusplus-analyzer/tests/pipeline_fixtures.py',
    'bazaarplusplus-server/src/modules/bundle-collection.ts'
  ],
  [
    'bazaarplusplus-mod/native/macos/build.sh',
    'bazaarplusplus-installer/scripts/bundle.ts'
  ]
];

export function unregisteredReferences(file: string, source: string): string[] {
  const consumer = PROJECTS.find((p) =>
    file.startsWith(`bazaarplusplus-${p}/`)
  );
  // Markdown links document ownership. Markdown used as a contract is listed
  // explicitly in SHARED_INPUTS; executable/config references are scanned here.
  if (!consumer || file.endsWith('.md') || source.includes('\0')) return [];
  const references = [
    ...source
      .replaceAll('\\', '/')
      .matchAll(
        /bazaarplusplus-(?:ui-docs|site|server|bot|analyzer|installer|mod|android)\/[\w./*-]*/g
      )
  ];
  return references.flatMap(([reference]) => {
    const target = path.posix.normalize(reference.replace(/[/.]+$/, ''));
    if (
      target === `bazaarplusplus-${consumer}` ||
      target.startsWith(`bazaarplusplus-${consumer}/`)
    )
      return [];
    if (
      INFORMATIONAL_REFERENCES.some(
        ([from, to]) => file === from && target === to
      )
    )
      return [];
    if (
      SHARED_INPUTS.some(
        ([input, consumers]) =>
          consumers.includes(consumer) &&
          covers(input, target + (reference.endsWith('/') ? '/' : ''))
      )
    )
      return [];
    if (
      SHARED_INPUTS.some(
        ([input, consumers]) =>
          consumers.includes(consumer) && input === `${target}/`
      )
    )
      return [];
    return [`${file} -> ${reference}`];
  });
}

const git = (args: readonly string[], cwd: string) =>
  runGit(args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024
  });

export function scanDependencies(cwd: string = process.cwd()): string[] {
  return git(['ls-files', '-z'], cwd)
    .split('\0')
    .filter((file) => file && fs.existsSync(path.join(cwd, file)))
    .flatMap((file) => {
      if (!PROJECTS.some((p) => file.startsWith(`bazaarplusplus-${p}/`)))
        return [];
      return unregisteredReferences(
        file,
        fs.readFileSync(path.join(cwd, file), 'utf8')
      );
    });
}

export function fullScope(reason: string): ChecksPlan {
  return Object.fromEntries(SCOPES.map((scope) => [scope, [reason]]));
}

const DESIGN_ALL_CONSUMERS = [
  'ui',
  'installer-frontend',
  'site',
  'ui-docs',
  'android',
  'bot',
  'server',
  'release'
];

const DOC_LINK_CHECKERS = ['release', 'installer-frontend', 'mod-macos'];

// Explicit consumer maps for root-owned release/ and scripts/ inputs. Unknown
// files under either directory fall through to fullScope (fail-open); tests
// verify every non-markdown reference from projects (plus transitive TS imports).
const RELEASE_INPUTS: [string, string[]][] = [
  ['release/test-support/', ['release']],
  [
    'release/payload.json',
    ['release', 'mod', 'mod-windows', 'installer', 'installer-windows']
  ],
  ['release/generated/Payload.targets', ['release', 'mod', 'mod-windows']],
  [
    'release/downloads.ts',
    ['release', 'bot', 'site', 'installer', 'installer-windows', 'mod']
  ],
  [
    'release/manifest.ts',
    ['release', 'android', 'installer', 'installer-windows']
  ],
  ['release/fixtures/latest.json', ['release', 'mod']],
  ['release/fixtures/latest/', ['release', 'android', 'mod', 'site']],
  ['release/fixtures/game-status.json', ['release', 'site']],
  ['release/fixtures/android-game-status.json', ['release', 'site']],
  ['release/fixtures/android-carried-release.json', ['release', 'android']],
  ['release/fixtures/android-interop/', ['release', 'android']],
  [
    'release/fixtures/managed-payload/manifest.txt',
    ['release', 'android', 'installer', 'installer-windows', 'mod']
  ],
  ['release/fixtures/managed-payload/', ['release', 'android', 'mod']],
  // Transitively imported by both mod/scripts and installer/scripts.
  ['release/product.ts', ['release', 'mod', 'installer', 'installer-windows']],
  [
    'release/game-assembly-snapshot.ts',
    ['release', 'mod', 'installer', 'installer-windows']
  ],
  [
    'release/snapshot-lock.ts',
    ['release', 'mod', 'installer', 'installer-windows']
  ],
  ['release/r2-store.ts', ['release', 'mod', 'installer', 'installer-windows']],
  [
    'release/snapshot-files.ts',
    ['release', 'mod', 'installer', 'installer-windows']
  ],
  // Transitively imported by mod/scripts only.
  ['release/game-libs.ts', ['release', 'mod']],
  ['release/android-adaptation.ts', ['release', 'mod']],
  ['release/game-status.ts', ['release', 'mod']],
  ['release/android-apk.ts', ['release', 'mod']],
  ['release/interop-snapshot.ts', ['release', 'mod']],
  ['release/official-apk.ts', ['release', 'mod']],
  // Transitively imported by installer/scripts only.
  [
    'release/release-platforms.ts',
    ['release', 'installer', 'installer-windows']
  ],
  ['release/projections.ts', ['release', 'installer', 'installer-windows']],
  ['release/payload.ts', ['release', 'installer', 'installer-windows']],
  ['release/payload-zip.ts', ['release', 'installer', 'installer-windows']],
  [
    'release/history-database.ts',
    ['release', 'installer', 'installer-windows']
  ],
  ['release/version-sync.ts', ['release', 'installer', 'installer-windows']],
  [
    'release/payload-inventory.ts',
    ['release', 'installer', 'installer-windows']
  ],
  [
    'release/native-recorder-input.ts',
    ['release', 'installer', 'installer-windows']
  ],
  [
    'release/artifact-manifest.ts',
    ['release', 'installer', 'installer-windows']
  ],
  ['release/managed-payload.ts', ['release', 'installer', 'installer-windows']],
  [
    'release/managed-payload-chunks.ts',
    ['release', 'installer', 'installer-windows']
  ],
  // Root release CLI, orchestration and metadata only.
  ['release/android-app.ts', ['release']],
  ['release/android-interop.ts', ['release']],
  ['release/android-release.ts', ['release']],
  ['release/card-art.ts', ['release']],
  ['release/cli.ts', ['release']],
  ['release/github-secrets.json', ['release']],
  ['release/github-secrets.ts', ['release']],
  ['release/mirror.ts', ['release']],
  ['release/mod-data.ts', ['release']],
  ['release/publish.ts', ['release']],
  ['release/release.just', ['release']]
];

const SCRIPT_INPUTS: [string, string[]][] = [
  ['scripts/test-support/', ['release', 'installer', 'installer-windows']],
  ['scripts/mod-validation.ts', ['release', 'mod', 'mod-windows']],
  ['scripts/verify.ts', ['release', 'mod', 'mod-windows']],
  ['scripts/dev/', ['release']],
  ['scripts/qqbot/', ['release']],
  ['scripts/android/', ['release']],
  ['scripts/workspace.ts', ['release']],
  ['scripts/local-config.ts', ['release']],
  ['scripts/secrets.ts', ['release']],
  ['scripts/vite-plus.ts', ['release']],
  ['scripts/install-hooks.ts', ['release']]
];

const GITHUB_SCRIPT_RELEASE_ONLY = new Set([
  '.github/scripts/analyzer-state.sh',
  '.github/scripts/check-mod-dependency-update.ts',
  '.github/scripts/codeql-scope.ts',
  '.github/scripts/dependabot-auto-merge.ts',
  '.github/scripts/release-secrets.ts'
]);

function matchInput(
  table: readonly [string, string[]][],
  file: string
): string[] | null {
  for (const [input, consumers] of table) {
    if (covers(input, file)) return consumers;
  }
  return null;
}

export function classifyChanges(changes: DiffChange[] | null): ChecksPlan {
  if (!Array.isArray(changes) || !changes.length)
    return fullScope('Diff unavailable or empty');
  const plan: ChecksPlan = Object.fromEntries(
    SCOPES.map((scope): [string, string[]] => [scope, []])
  );
  const select = (scope: string, reason: string) => {
    for (const lane of dailyScopes(scope)) {
      if (!plan[lane].includes(reason)) plan[lane].push(reason);
    }
  };
  for (const { status, file } of changes) {
    if (
      typeof file !== 'string' ||
      !/^[AMDT]$/.test(status) ||
      file.startsWith('/') ||
      file.split('/').includes('..')
    )
      return fullScope('Unrecognized diff entry');
    const project = PROJECTS.find((p) =>
      file.startsWith(`bazaarplusplus-${p}/`)
    );
    if (project) {
      const sharedConsumers = SHARED_INPUTS.filter(([input]) =>
        covers(input, file)
      ).flatMap(([, consumers]) => consumers);
      // Only mod (DocsHygieneTests, TenWinBuildMarkdownTests) and installer
      // (docs:check) verify their own Markdown; other projects' non-contract
      // docs only need root tooling checks (script-references.test.ts).
      if (
        file.endsWith('.md') &&
        project !== 'mod' &&
        project !== 'installer' &&
        sharedConsumers.length === 0
      ) {
        select('release', `Project documentation: ${file}`);
        continue;
      }
      select(
        project === 'installer' && installerFrontendOnly(file)
          ? 'installer-frontend'
          : project === 'mod' &&
              (file.endsWith('.md') ||
                (file.startsWith('bazaarplusplus-mod/src/BazaarPlusPlus/') &&
                  file.endsWith('.cs')))
            ? 'mod-macos'
            : project,
        `Project input: ${file}`
      );
      if (windowsBuildInput(project, file))
        select(`${project}-windows`, `Windows build input: ${file}`);
      if (
        file.endsWith('.just') ||
        file.startsWith('bazaarplusplus-mod/scripts/')
      )
        select('release', `Command recipe or tooling: ${file}`);
      for (const consumer of sharedConsumers)
        select(consumer, `Shared input: ${file}`);
    } else if (file.startsWith('packages/ui/')) {
      for (const consumer of ['ui', 'installer-frontend', 'site', 'ui-docs'])
        select(consumer, `UI kit: ${file}`);
      if (file === 'packages/ui/package.json' || file.endsWith('.just'))
        select('release', `UI kit config: ${file}`);
    } else if (file.startsWith('packages/runs/')) {
      for (const scope of ['installer-frontend', 'site', 'release'])
        select(scope, file);
    } else if (file.startsWith('design/')) {
      if (file.endsWith('.md')) {
        // Removing or replacing design docs can break links checked by mod and installer.
        const consumers =
          status === 'D' || status === 'T' ? DOC_LINK_CHECKERS : ['release'];
        for (const consumer of consumers)
          select(consumer, `Design documentation: ${file}`);
      } else if (
        file.startsWith('design/components/InGameKit/art/') ||
        file === 'design/kit-art.ts'
      ) {
        for (const consumer of ['mod', 'release'])
          select(consumer, `InGameKit design input: ${file}`);
      } else if (
        file === 'design/launcher-icon.ts' ||
        file.startsWith('design/assets/Covers/')
      ) {
        for (const consumer of ['android', 'release'])
          select(consumer, `Android design input: ${file}`);
      } else {
        // The release lane includes design::check, which verifies outputs consumed by the apps.
        for (const consumer of DESIGN_ALL_CONSUMERS)
          select(consumer, `Design system: ${file}`);
      }
    } else if (file.startsWith('release/')) {
      if (file.endsWith('.test.ts')) {
        select('release', `Release test: ${file}`);
      } else {
        const consumers = matchInput(RELEASE_INPUTS, file);
        if (!consumers) return fullScope(`Unknown release input: ${file}`);
        for (const consumer of consumers)
          select(consumer, `Release input: ${file}`);
      }
    } else if (file.startsWith('scripts/')) {
      if (file.endsWith('.test.ts') || file.endsWith('.md')) {
        select('release', `Workspace script test or doc: ${file}`);
      } else {
        const consumers = matchInput(SCRIPT_INPUTS, file);
        if (!consumers) return fullScope(`Shared or unknown script: ${file}`);
        for (const consumer of consumers)
          select(consumer, `Workspace script: ${file}`);
      }
    } else if (file.startsWith('.agents/') || file.startsWith('.claude/')) {
      select('release', `Agent configuration: ${file}`);
    } else if (file.startsWith('docs/') || /^[^/]+\.md$/.test(file)) {
      // Installer docs:check and mod DocsHygieneTests resolve links to root
      // docs. Removing/moving their targets needs those checks too; editing
      // prose only needs root tooling checks.
      if (status === 'D' || status === 'T') {
        for (const consumer of DOC_LINK_CHECKERS)
          select(consumer, `Removed or replaced root documentation: ${file}`);
      } else {
        select('release', `Root documentation: ${file}`);
      }
    } else if (
      ['.gitignore', '.gitattributes', '.github/dependabot.yml'].includes(
        file
      ) ||
      file.startsWith('.github/codeql/') ||
      (file.startsWith('.github/scripts/') &&
        (file.endsWith('.test.ts') || GITHUB_SCRIPT_RELEASE_ONLY.has(file))) ||
      (file.startsWith('.github/workflows/') &&
        !['checks.yml', 'release.yml'].includes(path.posix.basename(file)))
    ) {
      select('release', `Repository configuration: ${file}`);
    } else return fullScope(`Shared or unknown input: ${file}`);
  }
  return plan;
}

export function readChanges(
  eventName: string | undefined,
  event: ChecksEvent,
  cwd: string = process.cwd(),
  runGit: (args: readonly string[]) => string = (args) => git(args, cwd)
): DiffChange[] {
  let base: string | undefined;
  let head: string | undefined;
  const sha = (value: string | undefined): value is string =>
    typeof value === 'string' &&
    /^[a-f0-9]{40}$/.test(value) &&
    !/^0+$/.test(value);
  if (eventName === 'pull_request') {
    head = event.pull_request?.head?.sha;
    base = event.pull_request?.base?.sha;
    if (!sha(base) || !sha(head)) throw new Error('PR SHAs unavailable');
    base = runGit(['merge-base', base, head]).trim();
  } else if (eventName === 'push') {
    base = event.before;
    head = event.after;
  } else throw new Error(`Full run: ${eventName}`);
  if (!sha(base) || !sha(head))
    throw new Error('Diff SHAs unavailable (including a zero before SHA)');
  // --no-renames deliberately represents moves as delete+add: both paths are
  // classified, including moves across projects. NUL preserves unusual names.
  const raw = runGit([
    'diff',
    '--name-status',
    '--no-renames',
    '-z',
    base,
    head,
    '--'
  ]);
  if (!raw.endsWith('\0')) throw new Error('Empty or incomplete diff');
  const fields = raw.slice(0, -1).split('\0');
  if (fields.length % 2) throw new Error('Incomplete diff entry');
  const changes: DiffChange[] = [];
  for (let i = 0; i < fields.length; i += 2)
    changes.push({ status: fields[i], file: fields[i + 1] });
  return changes;
}

export function planRun({
  eventName,
  event,
  cwd,
  runGit,
  dependencyProblems = []
}: PlanRunOptions): PlanRunResult {
  const result = (plan: ChecksPlan, compatibility = false): PlanRunResult => {
    const desktop = ['mod-macos', 'mod-windows'].some(
      (scope) => plan[scope].length > 0
    );
    return {
      plan,
      compatibility: desktop && compatibility,
      performance: desktop && eventName === 'workflow_dispatch'
    };
  };
  if (dependencyProblems.length) {
    const reason = `Unregistered cross-project reference: ${dependencyProblems.join('; ')}`;
    // A partial manual request must not claim coverage when the dependency
    // boundary could not be verified. Automatic runs conservatively expand.
    if (eventName === 'workflow_dispatch') throw new Error(reason);
    return result(fullScope(reason), true);
  }
  if (eventName === 'workflow_dispatch')
    return result(manualScope(event.inputs), true);
  try {
    const changes = readChanges(eventName, event, cwd, runGit);
    return result(
      classifyChanges(changes),
      changes.some(
        ({ file }) => file === 'bazaarplusplus-mod/build/game-libs.lock.json'
      )
    );
  } catch (error) {
    return result(
      fullScope(`Conservative full run: ${(error as Error).message}`),
      true
    );
  }
}

if (import.meta.main) {
  const event = JSON.parse(
    fs.readFileSync(process.env.GITHUB_EVENT_PATH as string, 'utf8')
  ) as ChecksEvent;
  let dependencyProblems: string[];
  try {
    dependencyProblems = scanDependencies();
  } catch (error) {
    dependencyProblems = [
      `Dependency scan unavailable: ${(error as Error).message}`
    ];
  }
  if (process.env.HISTORY_FETCH_OUTCOME !== 'success')
    dependencyProblems.push('History fetch failed');
  const { plan, compatibility, performance } = planRun({
    eventName: process.env.GITHUB_EVENT_NAME,
    event,
    dependencyProblems
  });
  // JSON stringifies newlines in filenames before writing line-based outputs.
  const output = Object.entries(plan)
    .map(([scope, reasons]) => `${scope}=${reasons.length > 0}`)
    .join('\n');
  fs.appendFileSync(
    process.env.GITHUB_OUTPUT as string,
    `${output}\ncompatibility=${compatibility}\nperformance=${performance}\nplan=${JSON.stringify(plan)}\n`
  );
  console.log(JSON.stringify(plan, null, 2));
}
