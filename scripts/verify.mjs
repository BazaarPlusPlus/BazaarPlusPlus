import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { runGit } from './git-command.mjs';

export async function verify(recipes, cwd = process.cwd()) {
  if (
    !recipes.length ||
    recipes.some((name) => !/^[a-z][a-z0-9:-]*$/.test(name))
  ) {
    throw new Error('Usage: just verify <recipe> [recipe ...]');
  }
  const git = (...args) => runGit(args, { cwd }).trim();
  const head = git('rev-parse', 'HEAD');
  const directory = path.resolve(cwd, 'artifacts/verification', randomUUID());
  fs.mkdirSync(directory, { recursive: true });
  const resultPath = path.join(directory, 'result.json');
  const result = {
    head,
    dirty: git('status', '--porcelain').length > 0,
    cwd,
    status: 'running',
    startedAt: new Date().toISOString(),
    steps: []
  };
  const save = () =>
    fs.writeFileSync(resultPath, JSON.stringify(result, null, 2) + '\n');
  save();
  console.log(`Verification result: ${resultPath}`);
  for (const recipe of recipes) {
    const logPath = path.join(directory, `${result.steps.length + 1}.log`);
    const step = {
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
      await new Promise((resolve) => {
        const child = spawn('just', [recipe], {
          cwd,
          stdio: ['ignore', 'pipe', 'pipe']
        });
        child.stdout.on('data', (data) => {
          fs.writeSync(log, data);
          process.stdout.write(data);
        });
        child.stderr.on('data', (data) => {
          fs.writeSync(log, data);
          process.stderr.write(data);
        });
        child.on('error', (error) => {
          step.error = error.message;
        });
        // close waits for both process exit and output streams; a printed "Passed" is not completion.
        child.on('close', (code, signal) => {
          step.exitCode = code;
          step.signal = signal;
          step.status =
            code === 0 && !signal && !step.error ? 'passed' : 'failed';
          resolve();
        });
      });
    } finally {
      fs.closeSync(log);
    }
    save();
    if (step.status !== 'passed') break;
  }
  result.headAfter = git('rev-parse', 'HEAD');
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
    console.error(error.message);
    process.exitCode = 1;
  }
}
