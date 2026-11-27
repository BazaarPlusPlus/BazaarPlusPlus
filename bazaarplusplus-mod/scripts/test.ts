import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { withBuildLock } from './build-lock.ts';
import {
  MOD_ROOT,
  WORKSPACE_ROOT,
  collectProps,
  dotnetEnv,
  err,
  managedPath,
  managedProps,
  runDotnet,
  step
} from './mod-env.ts';

// Tests never deploy or fetch; they embed the committed seed fixtures.
const TEST_PROPS = [
  '-p:BppDeployToGame=false',
  '-p:ForceRemoteEmbeddedDataRefresh=false',
  `-p:RemoteEmbeddedDataDirectory=${path.join(MOD_ROOT, 'tests', 'TestData', 'remote-data')}`
];

function testDiagnostics(prefix: string): string[] {
  const base = path.join(MOD_ROOT, 'artifacts', 'tests');
  fs.mkdirSync(base, { recursive: true });
  const results = fs.mkdtempSync(path.join(base, `${prefix}.`));
  step(`Test evidence: ${results}`);
  return [
    '--logger',
    'trx',
    '--results-directory',
    results,
    '--blame-crash',
    '--blame-crash-dump-type',
    'mini',
    '--blame-hang-timeout',
    '5min',
    '--blame-hang-dump-type',
    'mini'
  ];
}

function resolveTestInputs(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): { props: string[]; managed: string } {
  const props = [...collectProps(args), ...managedProps(args, env)];
  const managed = managedPath(args, env);
  return { props, managed };
}

function cmdTest(args: readonly string[], env: NodeJS.ProcessEnv): number {
  const { props, managed } = resolveTestInputs(args, env);
  if (
    !managed ||
    !fs
      .statSync(path.join(managed, 'Assembly-CSharp.dll'), {
        throwIfNoEntry: false
      })
      ?.isFile()
  ) {
    throw new Error(
      `Game assemblies not found at '${managed || '<not discovered>'}'. Pass -p:ManagedPath=/absolute/path/to/Managed.`
    );
  }
  step(`Test game assemblies: ${managed}`);
  const diagnostics = testDiagnostics('desktop');
  return runDotnet(
    [
      'test',
      'tests/BazaarPlusPlus.Tests.slnx',
      '--filter',
      'TestKind!=Performance',
      ...diagnostics,
      ...TEST_PROPS,
      ...props
    ],
    { env }
  );
}

function cmdTestPerformance(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): number {
  const { props } = resolveTestInputs(args, env);
  const diagnostics = testDiagnostics('performance');
  return runDotnet(
    [
      'test',
      'tests/BazaarPlusPlus.Tests.slnx',
      '--filter',
      'TestKind=Performance',
      ...diagnostics,
      ...TEST_PROPS,
      ...props
    ],
    { env }
  );
}

function cmdTestCore(args: readonly string[], env: NodeJS.ProcessEnv): number {
  const props = collectProps(args);
  const coreProps = ['-p:BppSnapshotPlatform=none', ...TEST_PROPS];
  const rawList = execFileSync(
    'dotnet',
    [
      'msbuild',
      'Directory.Build.props',
      '-nologo',
      '-getProperty:BppTestCoreProjects',
      ...coreProps,
      ...props
    ],
    {
      cwd: MOD_ROOT,
      env: dotnetEnv(env),
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'inherit']
    }
  ).replace(/\s+/g, '');

  const projects = rawList.split(';').filter(Boolean);
  if (projects.length === 0) {
    throw new Error('BppTestCoreProjects in Directory.Build.props is empty.');
  }

  const diagnostics = testDiagnostics('core');
  const suiteCode = runDotnet(
    [
      'test',
      'tests/BazaarPlusPlus.CoreTests.slnx',
      ...diagnostics,
      ...coreProps,
      ...props
    ],
    { env }
  );
  if (suiteCode !== 0) return suiteCode;

  for (const project of projects) {
    const code = runDotnet(
      [
        'run',
        '--project',
        `tests/${project}/${project}.csproj`,
        ...coreProps,
        ...props
      ],
      { env }
    );
    if (code !== 0) return code;
  }
  return 0;
}

function cmdTestCorpus(
  args: readonly string[],
  env: NodeJS.ProcessEnv
): number {
  const usage =
    'Usage: just mod::test-corpus <replay-corpus-path> [report-path] [--benchmark]';
  let corpusPath = '';
  let reportPath = '';
  let benchmark = false;
  for (const arg of args) {
    if (arg === '--benchmark') {
      benchmark = true;
    } else if (!corpusPath) {
      corpusPath = arg;
    } else if (!reportPath) {
      reportPath = arg;
    } else {
      err(usage);
      return 2;
    }
  }
  if (!corpusPath) {
    err(usage);
    return 2;
  }
  if (!fs.statSync(corpusPath, { throwIfNoEntry: false })?.isDirectory()) {
    err(`Replay corpus directory not found: '${corpusPath}'.`);
    return 2;
  }

  const resolvedReport =
    reportPath ||
    path.join(MOD_ROOT, 'artifacts', 'combat-impact-corpus', 'report.json');
  const exported = `${resolvedReport.replace(/\.json$/, '')}.corpus.json.gz`;
  const props = managedProps([], env);

  const exportCode = runDotnet(
    [
      'run',
      '--project',
      'tests/CombatImpact.CorpusExport/CombatImpact.CorpusExport.csproj',
      '-p:BppDeployToGame=false',
      ...props,
      '--',
      'export',
      corpusPath,
      exported
    ],
    { env }
  );
  if (exportCode !== 0) return exportCode;

  return runDotnet(
    [
      'run',
      '--project',
      'tests/CombatImpact.Corpus/CombatImpact.Corpus.csproj',
      '-p:BppDeployToGame=false',
      '-p:BppSnapshotPlatform=none',
      '--',
      exported,
      resolvedReport,
      ...(benchmark ? ['--benchmark'] : [])
    ],
    { env }
  );
}

async function main(argv: readonly string[]): Promise<number> {
  const [command, ...rest] = argv;
  switch (command) {
    case 'test':
      return withBuildLock(WORKSPACE_ROOT, (env) => cmdTest(rest, env));
    case 'test-performance':
      return withBuildLock(WORKSPACE_ROOT, (env) =>
        cmdTestPerformance(rest, env)
      );
    case 'test-core':
      return withBuildLock(WORKSPACE_ROOT, (env) => cmdTestCore(rest, env));
    case 'test-corpus':
      return withBuildLock(WORKSPACE_ROOT, (env) => cmdTestCorpus(rest, env));
    default:
      throw new Error(
        "Usage: bun scripts/test.ts <test|test-performance|test-core|test-corpus> [args...]; see 'just --list mod'."
      );
  }
}

if (import.meta.main) {
  try {
    process.exitCode = await main(process.argv.slice(2));
  } catch (error) {
    err(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
