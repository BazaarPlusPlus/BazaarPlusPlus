import { execFileSync } from 'node:child_process';
import type { ExecFileSyncOptionsWithStringEncoding } from 'node:child_process';

// Git honors these variables before cwd or -C, so inherited values could redirect writes to another repository or index.
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

export interface RunGitOptions extends Omit<
  ExecFileSyncOptionsWithStringEncoding,
  'encoding'
> {
  encoding?: BufferEncoding;
}

export function gitEnvironment(
  baseEnv: NodeJS.ProcessEnv = process.env
): NodeJS.ProcessEnv {
  const env = { ...baseEnv };
  for (const name of REPOSITORY_LOCATION_VARIABLES) {
    delete env[name];
  }
  return env;
}

export function runGit(
  args: readonly string[],
  { env, ...options }: RunGitOptions = {}
): string {
  return execFileSync('git', args, {
    encoding: 'utf8',
    ...options,
    env: gitEnvironment(env)
  });
}
