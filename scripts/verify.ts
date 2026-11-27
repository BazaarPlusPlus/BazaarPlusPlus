import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import type { SpawnOptions } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { runGit } from './git-command.ts';

export type VerificationStatus = 'running' | 'passed' | 'failed';

/** One recipe in result.json; exitCode and signal stay null while it runs. */
export interface VerificationStep {
  recipe: string;
  logPath: string;
  status: VerificationStatus;
  exitCode: number | null;
  signal: NodeJS.Signals | null;
  error?: string;
}

export interface VerificationResult {
  head: string;
  dirty: boolean;
  cwd: string;
  status: VerificationStatus;
  startedAt: string;
  steps: VerificationStep[];
  headAfter?: string;
  finishedAt?: string;
}

export interface LoggedStepOutcome {
  exitCode: number | null;
  signal: NodeJS.Signals | null;
  error?: string;
}

export interface GitSnapshot {
  head: string;
  dirty: boolean;
}

export function captureGitSnapshot(cwd: string): GitSnapshot {
  const git = (...args: string[]): string => runGit(args, { cwd }).trim();
  return {
    head: git('rev-parse', 'HEAD'),
    dirty: git('status', '--porcelain').length > 0
  };
}

export function writeReceipt(file: string, data: unknown): void {
  fs.writeFileSync(file, JSON.stringify(data, null, 2) + '\n');
}

export function runCommand(
  command: string,
  args: readonly string[],
  options: SpawnOptions
): Promise<number> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, options);
    child.once('error', reject);
    child.once('close', (code) => resolve(code ?? 1));
  });
}

export async function runPipedToLog(
  command: string,
  args: readonly string[],
  logFd: number,
  options: { cwd: string; env?: NodeJS.ProcessEnv }
): Promise<LoggedStepOutcome> {
  return new Promise<LoggedStepOutcome>((resolve) => {
    let error: string | undefined;
    const child = spawn(command, args, {
      cwd: options.cwd,
      env: options.env,
      stdio: ['ignore', 'pipe', 'pipe']
    });
    child.stdout.on('data', (data: Buffer) => {
      fs.writeSync(logFd, data);
      process.stdout.write(data);
    });
    child.stderr.on('data', (data: Buffer) => {
      fs.writeSync(logFd, data);
      process.stderr.write(data);
    });
    child.on('error', (err) => {
      error = err.message;
    });
    // close waits for both process exit and output streams; a printed "Passed" is not completion.
    child.on('close', (exitCode, signal) => {
      resolve({ exitCode, signal, ...(error ? { error } : {}) });
    });
  });
}

export async function verify(
  recipes: readonly string[],
  cwd: string = process.cwd()
): Promise<number> {
  if (
    !recipes.length ||
    recipes.some((name) => !/^[a-z][a-z0-9:-]*$/.test(name))
  ) {
    throw new Error('Usage: just verify <recipe> [recipe ...]');
  }
  const { head, dirty } = captureGitSnapshot(cwd);
  const directory = path.resolve(cwd, 'artifacts/verification', randomUUID());
  fs.mkdirSync(directory, { recursive: true });
  const resultPath = path.join(directory, 'result.json');
  const result: VerificationResult = {
    head,
    dirty,
    cwd,
    status: 'running',
    startedAt: new Date().toISOString(),
    steps: []
  };
  const save = () => writeReceipt(resultPath, result);
  save();
  console.log(`Verification result: ${resultPath}`);
  for (const recipe of recipes) {
    const logPath = path.join(directory, `${result.steps.length + 1}.log`);
    const step: VerificationStep = {
      recipe,
      logPath,
      status: 'running',
      exitCode: null,
      signal: null
    };
    result.steps.push(step);
    save();
    const log = fs.openSync(logPath, 'w');
    try {
      const outcome = await runPipedToLog('just', [recipe], log, { cwd });
      step.exitCode = outcome.exitCode;
      step.signal = outcome.signal;
      if (outcome.error) step.error = outcome.error;
      step.status =
        outcome.exitCode === 0 && !outcome.signal && !outcome.error
          ? 'passed'
          : 'failed';
    } finally {
      fs.closeSync(log);
    }
    save();
    if (step.status !== 'passed') break;
  }
  result.headAfter = runGit(['rev-parse', 'HEAD'], { cwd }).trim();
  result.status =
    result.steps.length === recipes.length &&
    result.steps.every((step) => step.status === 'passed') &&
    result.headAfter === head
      ? 'passed'
      : 'failed';
  result.finishedAt = new Date().toISOString();
  save();
  console.log(`Verification ${result.status}: ${resultPath}`);
  return result.status === 'passed' ? 0 : 1;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    process.exitCode = await verify(process.argv.slice(2));
  } catch (error) {
    console.error((error as Error).message);
    process.exitCode = 1;
  }
}
