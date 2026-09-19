import { execFileSync } from 'node:child_process';
import {
  cpSync,
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

const GENERATED_COMMANDS_FILE = 'commands.ts';

// Every input whose change can alter the exported bindings.
const BINDING_INPUT_PATHS = Object.freeze([
  'src-tauri/src',
  'src-tauri/Cargo.toml',
  'src-tauri/Cargo.lock',
  'scripts/generate-bindings.mjs'
]);

// Pure staleness decision. `generatedMtimeMs` is null when the committed
// bindings are absent; `inputMtimesMs` is empty when no input could be read.
// Both cases force a regeneration rather than a silent skip.
export function bindingsAreFresh(generatedMtimeMs, inputMtimesMs) {
  if (generatedMtimeMs === null || inputMtimesMs.length === 0) {
    return false;
  }
  return inputMtimesMs.every((inputMtimeMs) => inputMtimeMs < generatedMtimeMs);
}

function collectMtimesMs(entryPath, collected) {
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

export function generatedBindingsAreFresh(projectRoot) {
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

export function replaceDirectoryWithBackup(
  targetDir,
  sourceDir,
  fileOperations = {}
) {
  const rename = fileOperations.renameSync ?? renameSync;
  const parentDir = path.dirname(targetDir);
  const baseName = path.basename(targetDir);
  const suffix = `${process.pid}.${Date.now()}`;
  const stagingDir = path.join(parentDir, `.${baseName}.next.${suffix}`);
  const backupDir = path.join(parentDir, `.${baseName}.backup.${suffix}`);
  let backupCreated = false;

  mkdirSync(parentDir, { recursive: true });
  rmSync(stagingDir, { recursive: true, force: true });
  rmSync(backupDir, { recursive: true, force: true });
  cpSync(sourceDir, stagingDir, { recursive: true });

  try {
    if (existsSync(targetDir)) {
      rename(targetDir, backupDir);
      backupCreated = true;
    }

    rename(stagingDir, targetDir);
    rmSync(backupDir, { recursive: true, force: true });
  } catch (error) {
    if (backupCreated || existsSync(backupDir)) {
      rmSync(targetDir, { recursive: true, force: true });
      rename(backupDir, targetDir);
    }
    rmSync(stagingDir, { recursive: true, force: true });
    throw error;
  }
}

export function normalizeGeneratedLineEndings(directory) {
  for (const entry of readdirSync(directory)) {
    const entryPath = path.join(directory, entry);
    if (statSync(entryPath).isDirectory()) {
      normalizeGeneratedLineEndings(entryPath);
      continue;
    }
    if (!entry.endsWith('.ts')) continue;

    const content = readFileSync(entryPath, 'utf8');
    const normalized = content.replace(/\r\n?/g, '\n').replace(/\n*$/, '\n');
    if (normalized !== content) writeFileSync(entryPath, normalized, 'utf8');
  }
}

export function validateGeneratedBindings(directory) {
  const commandsPath = path.join(directory, GENERATED_COMMANDS_FILE);
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

export function commitGeneratedBindings({ generatedDir, tempGeneratedDir }) {
  normalizeGeneratedLineEndings(tempGeneratedDir);
  validateGeneratedBindings(tempGeneratedDir);
  replaceDirectoryWithBackup(generatedDir, tempGeneratedDir);
}

export function runGenerateBindings(
  projectRoot,
  { runAllRustTests = false, skipIfFresh = false } = {}
) {
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
    execFileSync('cargo', cargoArgs, {
      cwd: projectRoot,
      stdio: 'inherit',
      env: {
        ...process.env,
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
      'Usage: generate-bindings.mjs [--with-rust-tests | --if-stale]'
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
