import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { runGit } from '../git-command.mjs';

// A test fixture repository must be hermetic in both directions: it may never
// be redirected into a real repository (handled by `runGit`, which scrubs
// GIT_DIR and friends), and it may never read or write the developer's own
// configuration. Refusing the system config and pointing the global config at
// an empty file keeps a fixture off the developer's identity, hooks, gpgsign
// and templates, and keeps `git config` writes inside the fixture.
let sharedEmptyGlobalConfig = null;

function emptyGlobalConfigPath() {
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

// The only supported way for a test to spawn git. `cwd` is required so a
// fixture command always names the temporary directory it operates on.
export function runFixtureGit(args, { cwd, ...options } = {}) {
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
