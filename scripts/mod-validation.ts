import fs from 'node:fs';
import path from 'node:path';
import { withBuildLock } from '../bazaarplusplus-mod/scripts/build-lock.ts';
import { captureGitSnapshot, runCommand, writeReceipt } from './verify.ts';

export { withBuildLock };

export interface GateSpawnOptions {
  cwd: string;
  env: NodeJS.ProcessEnv;
  stdio: ['ignore', number, number];
}

export type GateRunner = (
  command: string,
  args: readonly string[],
  options: GateSpawnOptions
) => Promise<number>;

/** One gate in result.json; exitCode stays null while the gate runs. */
export interface ValidationStep {
  command: string[];
  log: string;
  exitCode: number | null;
}

export interface ValidationReceipt {
  head: string;
  dirty: boolean;
  status: 'running' | 'failed' | 'passed';
  steps: ValidationStep[];
}

const root = path.resolve(import.meta.dirname, '..');
export const androidChecks: string[][] = [
  ['mod::check'],
  ['mod::fetch', 'android', 'publisher'],
  ['mod::fetch', 'android', 'play'],
  ['mod::android-build'],
  ['mod::android-build', '-p:BppAndroidSource=play'],
  ['mod::test-core'],
  ['mod::test'],
  ['mod::test-performance']
];

export const run = runCommand;

export async function validate(
  cwd: string,
  env: NodeJS.ProcessEnv,
  execute: GateRunner = runCommand
): Promise<number> {
  const base = path.join(cwd, 'bazaarplusplus-mod/artifacts/validation');
  fs.mkdirSync(base, { recursive: true });
  const out = fs.mkdtempSync(path.join(base, 'android-'));
  const { head, dirty } = captureGitSnapshot(cwd);
  const receiptPath = path.join(out, 'result.json');
  const receipt: ValidationReceipt = {
    head,
    dirty,
    status: 'running',
    steps: []
  };
  const save = () => writeReceipt(receiptPath, receipt);
  console.log(`Android validation: ${out}`);
  save();
  for (const [index, args] of androidChecks.entries()) {
    const log = path.join(out, `${index + 1}.log`);
    const step: ValidationStep = {
      command: ['just', ...args],
      log,
      exitCode: null
    };
    receipt.steps.push(step);
    save();
    console.log(`Running just ${args.join(' ')}`);
    const fd = fs.openSync(log, 'w');
    try {
      step.exitCode = await execute('just', args, {
        cwd,
        env,
        stdio: ['ignore', fd, fd]
      });
    } catch (error) {
      fs.writeSync(fd, (error as Error).message + '\n');
      step.exitCode = 1;
    } finally {
      fs.closeSync(fd);
    }
    if (step.exitCode !== 0) {
      receipt.status = 'failed';
      save();
      console.error(
        fs.readFileSync(log, 'utf8').split('\n').slice(-40).join('\n')
      );
      console.error(`Failed (exit ${step.exitCode}); full log: ${log}`);
      return step.exitCode;
    }
    save();
  }
  receipt.status = 'passed';
  save();
  console.log(`Android validation passed: ${receiptPath}`);
  return 0;
}

if (import.meta.main) {
  try {
    const [mode, ...args] = process.argv.slice(2);
    process.exitCode = await withBuildLock(root, (env) => {
      if (mode === 'check' && !args.length) return validate(root, env);
      if (mode === 'lock' && args[0] === '--' && args.length > 1)
        return runCommand(args[1], args.slice(2), {
          cwd: process.cwd(),
          env,
          stdio: 'inherit'
        });
      throw new Error(
        'Usage: mod-validation.ts check | lock -- <command> [args...]'
      );
    });
  } catch (error) {
    console.error((error as Error).message);
    process.exitCode = 1;
  }
}
