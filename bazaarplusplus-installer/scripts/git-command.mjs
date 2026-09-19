import { execFileSync } from 'node:child_process';

// Git decides *which* repository it is operating on from the environment before
// it ever looks at `cwd` or `-C`. Any of these variables, inherited from a
// parent process (an editor, a hook, an agent shell, an outer git command),
// silently redirects a child git invocation at a foreign repository or index:
// the command still reports success while reading and writing somewhere else
// entirely. Every git spawn in this repository targets a directory it chose
// explicitly, so the directory must win.
//
// Deliberately kept minimal: only variables that relocate the repository, the
// object store, or the index are scrubbed. `GIT_CEILING_DIRECTORIES` only
// limits upward discovery and `GIT_AUTHOR_*`/`GIT_COMMITTER_*` only supply an
// identity, so neither can send a command at the wrong repository.
const REPOSITORY_LOCATION_VARIABLES = [
  'GIT_DIR',
  'GIT_WORK_TREE',
  'GIT_COMMON_DIR',
  'GIT_INDEX_FILE',
  'GIT_OBJECT_DIRECTORY',
  'GIT_ALTERNATE_OBJECT_DIRECTORIES',
  'GIT_NAMESPACE',
  'GIT_PREFIX'
];

// Copy of `baseEnv` with every repository-relocating variable removed.
export function gitEnvironment(baseEnv = process.env) {
  const env = { ...baseEnv };
  for (const name of REPOSITORY_LOCATION_VARIABLES) {
    delete env[name];
  }
  return env;
}

// The single seam for spawning git. Callers pass the directory they mean via
// `cwd` (or `-C`) and never inherit a repository location from the environment.
export function runGit(args, { env, ...options } = {}) {
  return execFileSync('git', args, {
    encoding: 'utf8',
    ...options,
    env: gitEnvironment(env)
  });
}
