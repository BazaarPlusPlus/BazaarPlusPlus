import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { runGit } from './git-command.mjs';

const root = path.resolve(import.meta.dirname, '..');
export const androidChecks = [
  ['mod::check'],
  ['mod::fetch', 'android', 'online'],
  ['mod::android-build'],
  ['mod::test-core'],
  ['mod::test'],
  ['mod::test-performance']
];

export async function withBuildLock(cwd, action) {
  const directory = path.join(cwd, 'bazaarplusplus-mod/artifacts/build.lock');
  if (
    process.env.BPP_MOD_BUILD_LOCK &&
    path.resolve(process.env.BPP_MOD_BUILD_LOCK) === directory
  )
    return action(process.env);
  fs.mkdirSync(path.dirname(directory), { recursive: true });
  try {
    fs.mkdirSync(directory);
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    throw new Error(
      `Mod build is already locked: ${directory}. Wait for the other command; after an interrupted run, confirm its processes have stopped before removing this directory.`
    );
  }
  try {
    fs.writeFileSync(
      path.join(directory, 'owner.json'),
      JSON.stringify({ pid: process.pid, command: process.argv })
    );
    return await action({
      ...process.env,
      BPP_MOD_BUILD_LOCK: directory.split(path.sep).join('/')
    });
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
}

export function run(command, args, options) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, options);
    child.once('error', reject);
    child.once('close', (code) => resolve(code ?? 1));
  });
}

export async function validate(cwd, env, execute = run) {
  const base = path.join(cwd, 'bazaarplusplus-mod/artifacts/validation');
  fs.mkdirSync(base, { recursive: true });
  const out = fs.mkdtempSync(path.join(base, 'android-'));
  const git = (...args) => runGit(args, { cwd }).trim();
  const receipt = {
    head: git('rev-parse', 'HEAD'),
    dirty: git('status', '--porcelain').length > 0,
    status: 'running',
    steps: []
  };
  const save = () =>
    fs.writeFileSync(
      path.join(out, 'result.json'),
      JSON.stringify(receipt, null, 2) + '\n'
    );
  console.log(`Android validation: ${out}`);
  save();
  for (const [index, args] of androidChecks.entries()) {
    const log = path.join(out, `${index + 1}.log`);
    const step = { command: ['just', ...args], log, exitCode: null };
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
      fs.writeSync(fd, error.message + '\n');
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
  console.log(`Android validation passed: ${out}/result.json`);
  return 0;
}

if (import.meta.main) {
  try {
    const [mode, ...args] = process.argv.slice(2);
    process.exitCode = await withBuildLock(root, (env) => {
      if (mode === 'check' && !args.length) return validate(root, env);
      if (mode === 'lock' && args[0] === '--' && args.length > 1)
        return run(args[1], args.slice(2), {
          cwd: process.cwd(),
          env,
          stdio: 'inherit'
        });
      throw new Error(
        'Usage: mod-validation.mjs check | lock -- <command> [args...]'
      );
    });
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
