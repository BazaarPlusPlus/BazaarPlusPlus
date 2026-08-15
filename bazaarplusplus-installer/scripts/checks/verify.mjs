import path from 'node:path';
import process from 'node:process';
import { spawnSync } from 'node:child_process';

const manifestPath = 'src-tauri/Cargo.toml';

function npmStep(label, script, extraArgs = []) {
  return {
    label,
    command: 'npm',
    args: ['run', script, ...extraArgs]
  };
}

export function verificationSteps({ mode, releasePlatform }) {
  if (mode !== 'source' && mode !== 'release') {
    throw new Error(`Unsupported verification mode: ${mode}`);
  }

  const prebuildStep =
    mode === 'source'
      ? npmStep(
          'Source version and resource contract checks',
          'prebuild-check:source:after-bindings'
        )
      : npmStep(
          'Release version and payload checks',
          'prebuild-check:after-bindings',
          releasePlatform ? ['--', '--platform', releasePlatform] : []
        );

  return [
    npmStep('Generate bindings and run Rust tests', 'generate:bindings:test'),
    npmStep('Check generated binding freshness', 'check:bindings'),
    npmStep('Check formatting', 'format:check'),
    npmStep('Type-check TypeScript', 'check:ts'),
    npmStep('Run Vitest', 'test:unit'),
    {
      label: 'Check Rust formatting',
      command: 'cargo',
      args: ['fmt', '--manifest-path', manifestPath, '--', '--check']
    },
    {
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
    {
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
    npmStep('Build production frontend', 'build:frontend')
  ];
}

export function runVerification({
  rootDir,
  mode,
  releasePlatform,
  platform = process.platform,
  commandShell = process.env.ComSpec ?? 'cmd.exe',
  run = spawnSync,
  log = console.log
}) {
  for (const step of verificationSteps({ mode, releasePlatform })) {
    log(`==> ${step.label}`);
    const usesWindowsNpmShim = platform === 'win32' && step.command === 'npm';
    const command = usesWindowsNpmShim ? commandShell : step.command;
    const args = usesWindowsNpmShim
      ? ['/d', '/s', '/c', 'npm.cmd', ...step.args]
      : step.args;
    const result = run(command, args, {
      cwd: rootDir,
      stdio: step.stdio ?? 'inherit',
      env: { ...process.env, ...step.env }
    });
    if (result.error) {
      console.error(result.error.message);
      return 1;
    }
    if (result.status !== 0) {
      return result.status ?? 1;
    }
  }
  return 0;
}

function parseCliArgs(args) {
  let mode = 'release';
  let releasePlatform;

  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg === '--source-only') {
      mode = 'source';
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
  return { mode, releasePlatform };
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
