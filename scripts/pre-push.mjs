import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import {
  classifyChanges,
  fullScope,
  readChanges
} from '../.github/scripts/checks-scope.mjs';
import { runGit } from './git-command.mjs';

const zero = /^0+$/;

export function pushPlan(input, remote, cwd) {
  const git = (args) => runGit(args, { cwd }).trim();
  const changes = [];
  const lines = input.trim().split('\n').filter(Boolean);
  if (!lines.length) return fullScope('Push refs unavailable');
  try {
    for (const line of lines) {
      const fields = line.trim().split(/\s+/);
      if (fields.length !== 4) throw new Error('Malformed push ref');
      const [, local, , previous] = fields;
      if (zero.test(local)) continue; // Deleting a remote ref runs no code.
      const head = git(['rev-parse', `${local}^{commit}`]);
      const base = zero.test(previous)
        ? git(['symbolic-ref', `refs/remotes/${remote}/HEAD`])
        : previous;
      changes.push(
        ...readChanges(
          zero.test(previous) ? 'pull_request' : 'push',
          zero.test(previous)
            ? {
                pull_request: {
                  base: { sha: git(['rev-parse', base]) },
                  head: { sha: head }
                }
              }
            : { before: previous, after: head },
          cwd,
          (args) => runGit(args, { cwd })
        )
      );
    }
  } catch (error) {
    return fullScope(`Push diff unavailable: ${error.message.split('\n')[0]}`);
  }
  return changes.length ? classifyChanges(changes) : {};
}

export function pushCommands(plan) {
  const selected = (name) => plan[name]?.length > 0;
  const commands = [];
  if (selected('release'))
    commands.push([
      'commands-check',
      'design::check',
      'release::check',
      'release::test'
    ]);
  if (['mod-macos', 'mod-windows', 'mod-android'].some(selected))
    commands.push(['verify', 'mod::test', 'mod::test-performance']);
  if (
    [
      'installer-frontend',
      'installer-macos',
      'installer-windows',
      'macos-icon'
    ].some(selected)
  )
    commands.push(['installer::check', 'installer::test']);
  for (const project of ['site', 'server', 'analyzer']) {
    if (selected(project)) commands.push([`${project}::test`]);
  }
  return commands;
}

if (import.meta.main) {
  try {
    const cwd = path.resolve(import.meta.dirname, '..');
    const plan = pushPlan(
      fs.readFileSync(0, 'utf8'),
      process.argv[2] || 'origin',
      cwd
    );
    const commands = pushCommands(plan);
    console.log(
      `Push checks: ${commands.map((args) => args.join(' ')).join('; ') || 'none (ref deletion)'}`
    );
    for (const args of commands) {
      const result = spawnSync('just', args, { cwd, stdio: 'inherit' });
      if (result.error) throw result.error;
      if (result.status !== 0) {
        process.exitCode = result.status || 1;
        break;
      }
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
