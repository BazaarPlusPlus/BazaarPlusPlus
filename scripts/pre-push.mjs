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

// False also when the old tip is missing locally: the push rewrote history we
// cannot see, so it is not a fast-forward we can diff from.
function fastForward(previous, head, cwd) {
  try {
    runGit(['merge-base', '--is-ancestor', previous, head], {
      cwd,
      stdio: 'ignore'
    });
    return true;
  } catch {
    return false;
  }
}

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
      // A rewritten branch (rebase, then a force push) no longer contains its
      // remote tip, so diffing from that tip would select the default
      // branch's commits instead of this branch's. Compare it like a first
      // push, from where it leaves the default branch.
      const branchDiff =
        zero.test(previous) || !fastForward(previous, head, cwd);
      const base = branchDiff
        ? git(['symbolic-ref', `refs/remotes/${remote}/HEAD`])
        : previous;
      changes.push(
        ...readChanges(
          branchDiff ? 'pull_request' : 'push',
          branchDiff
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

// npm dependencies each gate needs; without them the gate fails on a missing
// module, so name the install step instead.
const NPM_DIRECTORIES = {
  'commands-check': '.',
  'release::test': '.',
  'installer::check': 'bazaarplusplus-installer',
  'site::test': 'bazaarplusplus-site',
  'server::test': 'bazaarplusplus-server'
};

export function missingDependencies(commands, cwd) {
  const directories = new Set(
    commands.flat().flatMap((gate) => NPM_DIRECTORIES[gate] ?? [])
  );
  return [...directories].filter(
    (directory) => !fs.existsSync(path.join(cwd, directory, 'node_modules'))
  );
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
    const missing = missingDependencies(commands, cwd);
    if (missing.length)
      throw new Error(
        `Push checks need npm dependencies in: ${missing.join(', ')}. Run \`just setup\`, or \`npm ci\` in each directory.`
      );
    for (const args of commands) {
      const result = spawnSync('just', args, { cwd, stdio: 'inherit' });
      if (result.error?.code === 'ENOENT')
        throw new Error(
          'Push checks need `just` on PATH; see docs/development.md.'
        );
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
