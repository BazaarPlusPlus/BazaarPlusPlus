import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test, vi } from 'vitest';

import { gitEnvironment } from '../git-command.mjs';
import { runFixtureGit } from './git-fixture.mjs';

// Every file under `directory`, as sorted `relative path:size` lines. Enough to
// notice a new ref, a rewritten HEAD, a config append, or a fresh object.
function directorySnapshot(directory) {
  return fs
    .readdirSync(directory, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => {
      const filePath = path.join(entry.parentPath, entry.name);
      return `${path.relative(directory, filePath)}:${fs.statSync(filePath).size}`;
    })
    .sort()
    .join('\n');
}

afterEach(() => {
  vi.unstubAllEnvs();
});

test('fixture git ignores an inherited repository location', () => {
  const temporaryRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-git-fixture-test-')
  );
  try {
    const decoyRepository = path.join(temporaryRoot, 'decoy.git');
    fs.mkdirSync(decoyRepository);
    runFixtureGit(['init', '--bare', '-q'], { cwd: decoyRepository });
    const decoyBefore = directorySnapshot(decoyRepository);

    // Exactly the incident: a parent process exported GIT_DIR (plus a work
    // tree) before the test suite ran.
    const workTree = path.join(temporaryRoot, 'decoy-worktree');
    fs.mkdirSync(workTree);
    vi.stubEnv('GIT_DIR', decoyRepository);
    vi.stubEnv('GIT_WORK_TREE', workTree);
    expect(gitEnvironment().GIT_DIR).toBeUndefined();
    expect(gitEnvironment().GIT_WORK_TREE).toBeUndefined();

    const fixtureRepository = path.join(temporaryRoot, 'fixture');
    fs.mkdirSync(fixtureRepository);
    fs.writeFileSync(path.join(fixtureRepository, 'input.txt'), 'fixture body');
    runFixtureGit(['init', '-q'], { cwd: fixtureRepository });
    runFixtureGit(['config', 'user.name', 'Fixture Test'], {
      cwd: fixtureRepository
    });
    runFixtureGit(['config', 'user.email', 'fixture@example.test'], {
      cwd: fixtureRepository
    });
    runFixtureGit(['add', '.'], { cwd: fixtureRepository });
    runFixtureGit(['commit', '-qm', 'fixture inputs'], {
      cwd: fixtureRepository
    });

    // The fixture repository is where it was asked to be, and holds the commit.
    expect(fs.existsSync(path.join(fixtureRepository, '.git', 'HEAD'))).toBe(
      true
    );
    expect(
      runFixtureGit(['log', '-1', '--pretty=%s'], {
        cwd: fixtureRepository
      }).trim()
    ).toBe('fixture inputs');
    expect(
      runFixtureGit(['config', 'user.name'], { cwd: fixtureRepository }).trim()
    ).toBe('Fixture Test');

    // The repository named by GIT_DIR never saw any of it.
    expect(directorySnapshot(decoyRepository)).toBe(decoyBefore);
    expect(
      fs.readFileSync(path.join(decoyRepository, 'config'), 'utf8')
    ).not.toMatch(/Fixture Test/);
    expect(fs.readFileSync(path.join(decoyRepository, 'HEAD'), 'utf8')).toBe(
      'ref: refs/heads/main\n'
    );
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
});

test('fixture git refuses a command without an explicit fixture cwd', () => {
  expect(() => runFixtureGit(['status'])).toThrow(/explicit cwd/);
});
