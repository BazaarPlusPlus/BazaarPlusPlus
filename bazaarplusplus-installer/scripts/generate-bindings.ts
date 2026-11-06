import { execFileSync } from 'node:child_process';
import {
  existsSync,
  mkdtempSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync
} from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { tauriSourceEnvironment } from './tauri-source-env.ts';

const GENERATED_COMMANDS_FILE = 'commands.ts';

// Runs the binding export; execFileSync fits it.
export type BindingExportRunner = (
  file: string,
  args: string[],
  options: { cwd: string; stdio: 'inherit'; env: NodeJS.ProcessEnv }
) => unknown;

export interface GeneratedBindingsDirs {
  generatedDir: string;
  tempGeneratedDir: string;
}

export interface RunGenerateBindingsOptions {
  runAllRustTests?: boolean;
  skipIfFresh?: boolean;
  run?: BindingExportRunner;
}

// Every input whose change can alter the exported bindings.
export const BINDING_INPUT_PATHS = Object.freeze([
  'src-tauri/src',
  'src-tauri/Cargo.toml',
  'src-tauri/Cargo.lock',
  'scripts/generate-bindings.ts'
]);

// Pure staleness decision. `generatedMtimeMs` is null when the committed
// bindings are absent; `inputMtimesMs` is empty when no input could be read.
// Both cases force a regeneration rather than a silent skip.
export function bindingsAreFresh(
  generatedMtimeMs: number | null,
  inputMtimesMs: readonly number[]
): boolean {
  if (generatedMtimeMs === null || inputMtimesMs.length === 0) {
    return false;
  }
  return inputMtimesMs.every((inputMtimeMs) => inputMtimeMs < generatedMtimeMs);
}

function collectMtimesMs(entryPath: string, collected: number[]): number[] {
  let stats;
  try {
    stats = statSync(entryPath);
  } catch {
    return collected;
  }
  collected.push(stats.mtimeMs);
  if (stats.isDirectory()) {
    for (const entry of readdirSync(entryPath)) {
      collectMtimesMs(path.join(entryPath, entry), collected);
    }
  }
  return collected;
}

export function generatedBindingsAreFresh(projectRoot: string): boolean {
  const commandsPath = path.join(
    projectRoot,
    'src/types/generated',
    GENERATED_COMMANDS_FILE
  );
  const generatedMtimeMs = existsSync(commandsPath)
    ? statSync(commandsPath).mtimeMs
    : null;
  const inputMtimesMs = BINDING_INPUT_PATHS.flatMap((relativePath) =>
    collectMtimesMs(path.join(projectRoot, relativePath), [])
  );
  return bindingsAreFresh(generatedMtimeMs, inputMtimesMs);
}

function normalizeLineEndings(content: string): string {
  return content.replace(/\r\n?/g, '\n').replace(/\n*$/, '\n');
}

function validateGeneratedBindings(commandsPath: string): void {
  if (!existsSync(commandsPath)) {
    throw new Error(
      `Generated bindings did not contain ${GENERATED_COMMANDS_FILE}`
    );
  }

  const source = readFileSync(commandsPath, 'utf8');
  if (source.trim().length === 0 || !source.includes('export const commands')) {
    throw new Error(`${GENERATED_COMMANDS_FILE} was empty or malformed`);
  }
}

// Writes the export beside the committed file and renames it over the target,
// so a failed or invalid export leaves the previous bindings untouched.
export function commitGeneratedBindings({
  generatedDir,
  tempGeneratedDir
}: GeneratedBindingsDirs): void {
  const exportedPath = path.join(tempGeneratedDir, GENERATED_COMMANDS_FILE);
  const targetPath = path.join(generatedDir, GENERATED_COMMANDS_FILE);
  const stagedPath = `${targetPath}.tmp`;

  validateGeneratedBindings(exportedPath);
  mkdirSync(generatedDir, { recursive: true });
  try {
    writeFileSync(
      stagedPath,
      normalizeLineEndings(readFileSync(exportedPath, 'utf8')),
      'utf8'
    );
    renameSync(stagedPath, targetPath);
  } finally {
    rmSync(stagedPath, { force: true });
  }
}

export function runGenerateBindings(
  projectRoot: string,
  {
    runAllRustTests = false,
    skipIfFresh = false,
    run = execFileSync
  }: RunGenerateBindingsOptions = {}
): void {
  if (skipIfFresh && generatedBindingsAreFresh(projectRoot)) {
    console.log(
      'generate-bindings: skipped, committed bindings are newer than every Rust input'
    );
    return;
  }

  const generatedDir = path.join(projectRoot, 'src/types/generated');
  const tempRoot = mkdtempSync(path.join(tmpdir(), 'bpp-bindings-'));
  const tempGeneratedDir = path.join(tempRoot, 'generated');
  const exportPath = path.join(tempGeneratedDir, GENERATED_COMMANDS_FILE);

  mkdirSync(tempGeneratedDir, { recursive: true });

  try {
    const cargoArgs = [
      'test',
      '--locked',
      ...(runAllRustTests ? [] : ['export_bindings']),
      '--manifest-path',
      'src-tauri/Cargo.toml',
      '--',
      '--nocapture'
    ];
    run('cargo', cargoArgs, {
      cwd: projectRoot,
      stdio: 'inherit',
      env: {
        ...tauriSourceEnvironment(),
        BPP_SPECTA_EXPORT_PATH: exportPath
      }
    });

    commitGeneratedBindings({ generatedDir, tempGeneratedDir });
  } finally {
    rmSync(tempRoot, { recursive: true, force: true });
  }
}

if (import.meta.main) {
  const args = process.argv.slice(2);
  const allowedFlags = new Set(['--with-rust-tests', '--if-stale']);
  if (args.some((arg) => !allowedFlags.has(arg))) {
    console.error(
      'Usage: generate-bindings.ts [--with-rust-tests | --if-stale]'
    );
    process.exit(2);
  }
  if (args.includes('--with-rust-tests') && args.includes('--if-stale')) {
    console.error('--if-stale cannot be combined with --with-rust-tests');
    process.exit(2);
  }
  runGenerateBindings(path.resolve(import.meta.dirname, '..'), {
    runAllRustTests: args.includes('--with-rust-tests'),
    skipIfFresh: args.includes('--if-stale')
  });
}
