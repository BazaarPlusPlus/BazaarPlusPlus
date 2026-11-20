import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { runGit } from '../git-command.ts';
import type { RunGitOptions } from '../git-command.ts';

// Isolate fixture Git config from the developer's identity, hooks, signing and templates.
let sharedEmptyGlobalConfig: string | null = null;

function emptyGlobalConfigPath(): string {
  if (!sharedEmptyGlobalConfig) {
    const directory = fs.mkdtempSync(
      path.join(os.tmpdir(), 'bpp-git-fixture-config-')
    );
    sharedEmptyGlobalConfig = path.join(directory, 'gitconfig');
    fs.writeFileSync(sharedEmptyGlobalConfig, '');
    process.on('exit', () => {
      fs.rmSync(directory, { recursive: true, force: true });
    });
  }
  return sharedEmptyGlobalConfig;
}

export type RunFixtureGitOptions = RunGitOptions;

export function runFixtureGit(
  args: readonly string[],
  { cwd, ...options }: RunFixtureGitOptions = {}
): string {
  if (!cwd) {
    throw new Error(
      'runFixtureGit requires an explicit cwd inside the fixture directory'
    );
  }
  return runGit(['-c', 'init.defaultBranch=main', ...args], {
    ...options,
    cwd,
    env: {
      ...process.env,
      GIT_CONFIG_NOSYSTEM: '1',
      GIT_CONFIG_GLOBAL: emptyGlobalConfigPath()
    }
  });
}
