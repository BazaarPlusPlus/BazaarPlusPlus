import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

export const PROJECTS = ['site', 'server', 'analyzer', 'installer', 'mod'];
const PROJECT_SCOPES = {
  installer: ['installer-frontend', 'installer-macos', 'installer-windows'],
  mod: ['mod-macos', 'mod-windows']
};
export const SCOPES = [
  'release',
  ...PROJECTS.flatMap((project) => PROJECT_SCOPES[project] ?? [project]),
  'macos-icon'
];

// Only known frontend inputs may avoid native verification. New build inputs
// default to native; generated bindings must be checked against a real export.
export function installerFrontendOnly(file) {
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
      '.oxlintrc.json',
      '.prettierrc.json',
      '.prettierignore',
      'LICENSE'
    ].includes(relative)
  );
}

export function manualSelection(inputs = {}) {
  const scope = inputs.scope ?? 'full';
  const platform = inputs.platform ?? 'all';
  if (
    !['full', 'installer', 'mod'].includes(scope) ||
    !['all', 'macos', 'windows'].includes(platform)
  ) {
    throw new Error(`Invalid manual selection: ${scope}/${platform}`);
  }
  return { scope, platform };
}

export function manualScope(inputs) {
  const { scope, platform } = manualSelection(inputs);
  return Object.fromEntries(
    SCOPES.map((lane) => {
      const inScope =
        scope === 'full' ||
        lane.startsWith(`${scope}-`) ||
        (scope === 'installer' && lane === 'macos-icon');
      const inPlatform =
        platform === 'all' ||
        (!lane.endsWith('-macos') &&
          !lane.endsWith('-windows') &&
          lane !== 'macos-icon') ||
        lane.endsWith(`-${platform}`) ||
        (platform === 'macos' && lane === 'macos-icon');
      return [
        lane,
        inScope && inPlatform ? [`Manual selection: ${scope}/${platform}`] : []
      ];
    })
  );
}

// Path expansions, not another contract definition. Owners are documented in
// AGENTS.md; executable references are checked below on every classification.
export const SHARED_INPUTS = [
  ['bazaarplusplus-server/contracts/', ['mod', 'analyzer']],
  ['bazaarplusplus-server/docs/api-reference.md', ['mod']],
  ['bazaarplusplus-analyzer/contracts/', ['site', 'mod', 'installer']],
  [
    'bazaarplusplus-analyzer/docs/specs/consumer-data-contract.md',
    ['site', 'mod', 'installer']
  ],
  [
    'bazaarplusplus-mod/docs/contracts/run-payload-v5.md',
    ['server', 'analyzer']
  ],
  [
    'bazaarplusplus-mod/tests/BundleV5Codec.Tests/fixtures/',
    ['server', 'analyzer']
  ],
  [
    'bazaarplusplus-mod/tests/BundleQueueSqliteStore.Tests/fixtures/',
    ['installer']
  ],
  [
    'bazaarplusplus-mod/tests/RunLoggingPipeline.Tests/fixtures/',
    ['installer', 'release']
  ],
  ['bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BundleQueue/', ['installer']],
  [
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs',
    ['installer', 'release']
  ],
  [
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json',
    ['installer', 'release']
  ],
  ['bazaarplusplus-mod/build/game-libs.lock.json', ['release']],
  ['bazaarplusplus-mod/global.json', ['release']],
  ['bazaarplusplus-installer/src/styles/tokens.css', ['site']],
  ['bazaarplusplus-installer/static/support/', ['site']],
  ['bazaarplusplus-installer/src-tauri/resources/', ['mod']],
  ['bazaarplusplus-installer/scripts/headless.mjs', ['mod']],
  [
    'bazaarplusplus-installer/src-tauri/history-database-compatibility.json',
    ['mod', 'release']
  ],
  ...[
    'package.json',
    'package-lock.json',
    'src-tauri/Cargo.toml',
    'src-tauri/Cargo.lock',
    'src-tauri/tauri.conf.json',
    'src-tauri/tauri.macos.conf.json',
    'src-tauri/tauri.windows.conf.json'
  ].map((file) => [`bazaarplusplus-installer/${file}`, ['release']]),
  ['bazaarplusplus-installer/src-tauri/icons/source/', ['macos-icon']],
  [
    'bazaarplusplus-installer/scripts/release/compile-macos-icon.mjs',
    ['macos-icon']
  ]
];

function covers(input, file) {
  return file === input || (input.endsWith('/') && file.startsWith(input));
}

// These are comments pointing to the opposite side of a contract, not reads.
const INFORMATIONAL_REFERENCES = [
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
    'bazaarplusplus-installer/scripts/bundle.sh'
  ]
];

export function unregisteredReferences(file, source) {
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
        /bazaarplusplus-(?:site|server|analyzer|installer|mod)\/[\w./*-]*/g
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
    // Directory reads such as Vite's fs.allow cover all files in that directory.
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

const git = (args, cwd) =>
  execFileSync('git', args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024
  });

export function scanDependencies(cwd = process.cwd()) {
  return git(['ls-files', '-z'], cwd)
    .split('\0')
    .filter(Boolean)
    .flatMap((file) => {
      if (!PROJECTS.some((p) => file.startsWith(`bazaarplusplus-${p}/`)))
        return [];
      return unregisteredReferences(
        file,
        fs.readFileSync(path.join(cwd, file), 'utf8')
      );
    });
}

export function fullScope(reason) {
  return Object.fromEntries(SCOPES.map((scope) => [scope, [reason]]));
}

export function classifyChanges(changes) {
  if (!Array.isArray(changes) || !changes.length)
    return fullScope('Diff unavailable or empty');
  const plan = Object.fromEntries(SCOPES.map((scope) => [scope, []]));
  const select = (scope, reason) => {
    for (const lane of PROJECT_SCOPES[scope] ?? [scope]) {
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
    if (file.endsWith('.just'))
      return fullScope(`Shared command recipe: ${file}`);
    if (project) {
      select(
        project === 'installer' && installerFrontendOnly(file)
          ? 'installer-frontend'
          : project,
        `Project input: ${file}`
      );
      for (const [input, consumers] of SHARED_INPUTS) {
        if (covers(input, file))
          for (const consumer of consumers)
            select(consumer, `Shared input: ${file}`);
      }
    } else if (file.startsWith('docs/') || /^[^/]+\.md$/.test(file)) {
      // Project docs checks resolve links to root docs. Removing/moving their
      // targets needs those checks too; editing the target's prose does not.
      if (status === 'D' || status === 'T')
        return fullScope(`Removed or replaced root documentation: ${file}`);
      select('release', `Root documentation: ${file}`);
    } else return fullScope(`Shared or unknown input: ${file}`);
  }
  return plan;
}

export function readChanges(
  eventName,
  event,
  cwd = process.cwd(),
  runGit = (args) => git(args, cwd)
) {
  let base;
  let head;
  const sha = (value) =>
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
  const changes = [];
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
}) {
  if (dependencyProblems.length) {
    const reason = `Unregistered cross-project reference: ${dependencyProblems.join('; ')}`;
    // A partial manual request must not claim coverage when the dependency
    // boundary could not be verified. Automatic runs conservatively expand.
    if (eventName === 'workflow_dispatch') throw new Error(reason);
    return fullScope(reason);
  }
  if (eventName === 'workflow_dispatch') return manualScope(event.inputs);
  try {
    return classifyChanges(readChanges(eventName, event, cwd, runGit));
  } catch (error) {
    return fullScope(`Conservative full run: ${error.message}`);
  }
}

if (import.meta.main) {
  const event = JSON.parse(
    fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')
  );
  let dependencyProblems;
  try {
    dependencyProblems = scanDependencies();
  } catch (error) {
    dependencyProblems = [`Dependency scan unavailable: ${error.message}`];
  }
  if (process.env.HISTORY_FETCH_OUTCOME !== 'success')
    dependencyProblems.push('History fetch failed');
  const plan = planRun({
    eventName: process.env.GITHUB_EVENT_NAME,
    event,
    dependencyProblems
  });
  // JSON stringifies newlines in filenames before writing line-based outputs.
  const output = Object.entries(plan)
    .map(([scope, reasons]) => `${scope}=${reasons.length > 0}`)
    .join('\n');
  fs.appendFileSync(
    process.env.GITHUB_OUTPUT,
    `${output}\nplan=${JSON.stringify(plan)}\n`
  );
  console.log(JSON.stringify(plan, null, 2));
}
