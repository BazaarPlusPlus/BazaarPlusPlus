import path from 'node:path';
import process from 'node:process';
import { spawnSync } from 'node:child_process';
import type { StdioOptions } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { tauriSourceEnvironment } from '../tauri-source-env.ts';

export type VerificationMode = 'source' | 'release';

// The subset a caller asks for.
export type VerificationSubset = 'all' | 'frontend' | 'native';

// The subset a step belongs to: shared steps run in every subset, scripts in
// frontend (and native on Windows), release only in release mode.
export type VerificationStepSubset =
  | 'frontend'
  | 'native'
  | 'shared'
  | 'scripts'
  | 'release';

export interface VerificationStep {
  subset: VerificationStepSubset;
  label: string;
  command: string;
  args: string[];
  stdio?: StdioOptions;
  env?: Record<string, string>;
}

export interface VerificationStepsOptions {
  mode: VerificationMode;
  releasePlatform?: string;
  subset?: VerificationSubset;
  platform?: NodeJS.Platform;
}

// The part of a spawnSync result the runner reads.
export interface StepRunResult {
  status?: number | null;
  error?: Error;
}

// Runs one step; spawnSync fits it.
export type StepRunner = (
  command: string,
  args: string[],
  options: { cwd: string; stdio: StdioOptions; env: NodeJS.ProcessEnv }
) => StepRunResult;

export interface RunVerificationOptions extends VerificationStepsOptions {
  rootDir: string;
  run?: StepRunner;
  log?: (message: string) => void;
  now?: () => number;
  summaryPath?: string | null;
}

export interface VerifyCliOptions {
  mode: VerificationMode;
  releasePlatform: string | undefined;
  subset: VerificationSubset;
}

const manifestPath = 'src-tauri/Cargo.toml';

function bunStep(
  subset: VerificationStepSubset,
  label: string,
  script: string,
  extraArgs: string[] = []
): VerificationStep {
  return {
    subset,
    label,
    command: 'bun',
    args: ['run', script, ...extraArgs]
  };
}

export function verificationSteps({
  mode,
  releasePlatform,
  subset = 'all',
  platform = process.platform
}: VerificationStepsOptions): VerificationStep[] {
  if (mode !== 'source' && mode !== 'release') {
    throw new Error(`Unsupported verification mode: ${String(mode)}`);
  }

  if (!['all', 'frontend', 'native'].includes(subset)) {
    throw new Error(`Unsupported verification subset: ${subset}`);
  }
  if (mode === 'release' && subset !== 'all') {
    throw new Error('Release verification requires the full set of checks');
  }

  const prebuildStep =
    mode === 'source'
      ? bunStep(
          'shared',
          'Source version and resource contract checks',
          'prebuild-check:source:after-bindings'
        )
      : bunStep(
          'shared',
          'Release version and payload checks',
          'prebuild-check:after-bindings',
          releasePlatform ? ['--platform', releasePlatform] : []
        );

  return (
    [
      // Cheap static gates run first so the most common failures (formatting,
      // lint, a stale Cargo lock) are reported before the Rust test build starts.
      // Clippy only type-checks, so it also runs ahead of the test build.
      bunStep('frontend', 'Check formatting', 'format:check'),
      bunStep('frontend', 'Lint TypeScript and scripts', 'lint'),
      {
        subset: 'native',
        label: 'Check Rust formatting',
        command: 'cargo',
        args: ['fmt', '--manifest-path', manifestPath, '--', '--check']
      },
      {
        subset: 'native',
        label: 'Check locked Cargo dependency graph',
        command: 'cargo',
        args: [
          'metadata',
          '--locked',
          '--manifest-path',
          manifestPath,
          '--format-version',
          '1',
          '--no-deps'
        ],
        stdio: ['inherit', 'ignore', 'inherit']
      },
      {
        subset: 'native',
        label: 'Run Rust Clippy',
        command: 'cargo',
        args: [
          'clippy',
          '--locked',
          '--manifest-path',
          manifestPath,
          '--all-targets',
          '--all-features',
          '--',
          '-D',
          'warnings'
        ]
      },
      bunStep(
        'native',
        'Generate bindings and run Rust tests',
        'generate:bindings:test'
      ),
      bunStep('native', 'Check generated binding freshness', 'check:bindings'),
      bunStep('frontend', 'Type-check TypeScript', 'check:ts'),
      bunStep('frontend', 'Run frontend tests', 'test:unit', ['src']),
      // Linux covers script behavior too; Windows repeats this boundary because
      // process shims, paths and packaging scripts have Windows-specific code.
      bunStep('scripts', 'Run script tests', 'test:unit', ['scripts']),
      bunStep('frontend', 'Check documentation', 'docs:check'),
      {
        subset: 'release',
        label: 'Build strict Rust documentation',
        command: 'cargo',
        args: [
          'doc',
          '--locked',
          '--manifest-path',
          manifestPath,
          '--all-features',
          '--no-deps',
          '--document-private-items'
        ],
        env: { RUSTDOCFLAGS: '-D warnings' }
      },
      prebuildStep,
      bunStep('frontend', 'Build production frontend', 'build:frontend')
    ] satisfies VerificationStep[]
  ).filter(
    (step) =>
      (step.subset !== 'release' || mode === 'release') &&
      (subset === 'all' ||
        step.subset === 'shared' ||
        step.subset === subset ||
        (step.subset === 'scripts' &&
          (subset === 'frontend' || platform === 'win32')))
  );
}

export function runVerification({
  rootDir,
  mode,
  releasePlatform,
  subset = 'all',
  platform = process.platform,
  run = spawnSync,
  log = console.log,
  now = () => performance.now(),
  summaryPath = process.env.GITHUB_STEP_SUMMARY
}: RunVerificationOptions): number {
  const summarize = (text: string) => {
    if (!summaryPath) return;
    try {
      appendFileSync(summaryPath, text);
    } catch (error) {
      // Timing output must not change the verification result.
      console.warn(
        `Could not write verification timings: ${(error as Error).message}`
      );
    }
  };
  summarize(
    '\n### Installer verification timings\n\n' +
      'Rust tests include compilation, execution and binding generation.\n\n' +
      '| Step | Seconds | Result |\n| --- | ---: | --- |\n'
  );
  const environment =
    mode === 'source' ? tauriSourceEnvironment() : { ...process.env };
  for (const step of verificationSteps({
    mode,
    releasePlatform,
    subset,
    platform
  })) {
    log(`==> ${step.label}`);
    const started = now();
    const result = run(step.command, step.args, {
      cwd: rootDir,
      stdio: step.stdio ?? 'inherit',
      env: { ...environment, ...step.env }
    });
    const seconds = ((now() - started) / 1000).toFixed(2);
    const status = result.error ? 1 : (result.status ?? 1);
    log(`==> ${step.label}: ${seconds}s (exit ${status})`);
    summarize(
      `| ${step.label} | ${seconds} | ${status === 0 ? 'passed' : `failed (${status})`} |\n`
    );
    if (result.error) {
      console.error(result.error.message);
      return 1;
    }
    if (status !== 0) return status;
  }
  return 0;
}

export function parseCliArgs(args: readonly string[]): VerifyCliOptions {
  let mode: VerificationMode = 'release';
  let releasePlatform: string | undefined;
  let subset: VerificationSubset = 'all';

  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg === '--source-only') {
      mode = 'source';
      continue;
    }
    if (arg === '--subset') {
      subset = args[++index] as VerificationSubset;
      if (!['all', 'frontend', 'native'].includes(subset)) {
        throw new Error('--subset must be all, frontend or native');
      }
      continue;
    }
    if (arg === '--release-platform') {
      releasePlatform = args[index + 1];
      if (!releasePlatform) throw new Error('--release-platform needs a value');
      mode = 'release';
      index += 1;
      continue;
    }
    throw new Error(`Unknown verify argument: ${arg}`);
  }

  if (mode === 'source' && releasePlatform) {
    throw new Error('--source-only cannot be combined with --release-platform');
  }
  return { mode, releasePlatform, subset };
}

if (import.meta.main) {
  try {
    const options = parseCliArgs(process.argv.slice(2));
    process.exitCode = runVerification({
      rootDir: path.resolve(import.meta.dirname, '..', '..'),
      ...options
    });
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 2;
  }
}
