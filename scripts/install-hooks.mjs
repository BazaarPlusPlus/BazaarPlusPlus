import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { gitEnvironment, runGit } from './git-command.mjs';

const root = path.resolve(import.meta.dirname, '..');
const git = (...args) => runGit(args, { cwd: root }).trim();

// Resolve existing symlinks, including parents of a not-yet-created hooks dir.
// A dangling symlink must fail rather than be mistaken for a missing directory.
function resolvePath(file) {
  try {
    fs.lstatSync(file);
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    return path.join(resolvePath(path.dirname(file)), path.basename(file));
  }
  return fs.realpathSync(file);
}

function requireInside(directory, file) {
  const resolved = resolvePath(file);
  const relative = path.relative(directory, resolved);
  if (
    !relative ||
    relative === '..' ||
    relative.startsWith(`..${path.sep}`) ||
    path.isAbsolute(relative)
  ) {
    throw new Error(
      `Refusing hooks path outside repository git directory: ${file}`
    );
  }
  return resolved;
}

try {
  const commonDir = fs.realpathSync(
    git('rev-parse', '--path-format=absolute', '--git-common-dir')
  );
  const hooksDir = path.join(commonDir, 'hooks');
  const resolvedHooks = requireInside(commonDir, hooksDir);

  let configured;
  try {
    configured = git('config', '--local', '--get', 'core.hooksPath');
  } catch (error) {
    if (error.status !== 1) throw error;
  }
  if (configured !== hooksDir) {
    git('config', '--local', '--replace-all', 'core.hooksPath', hooksDir);
  }

  // Worktree or command-scope configuration can override the local setting.
  const effective = git(
    'rev-parse',
    '--path-format=absolute',
    '--git-path',
    'hooks'
  );
  if (requireInside(commonDir, effective) !== resolvedHooks) {
    throw new Error(`Refusing overridden hooks path: ${effective}`);
  }

  execFileSync(
    process.execPath,
    [
      path.join(root, 'node_modules/lefthook/bin/index.js'),
      'install',
      '--force'
    ],
    { cwd: root, env: gitEnvironment(), stdio: 'inherit' }
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = error.status || 1;
}
