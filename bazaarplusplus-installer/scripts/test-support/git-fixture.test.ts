import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test, vi } from 'vite-plus/test';

import { gitEnvironment } from '../../../scripts/git-command.ts';
import { runFixtureGit } from '../../../scripts/test-support/git-fixture.ts';

function directorySnapshot(directory: string): string {
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

    // Set inherited `GIT_DIR` to exercise Git's repository override.
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
