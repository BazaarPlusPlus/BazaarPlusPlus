import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'vite-plus/test';
import type { TestContext } from 'vite-plus/test';
import {
  CENTRAL_PACKAGES,
  TEST_TOOLS,
  validateChanges
} from './check-mod-dependency-update.ts';
import { runFixtureGit } from '../../scripts/test-support/git-fixture.ts';

function packages(name: string, version = '1.0.0', extra = ''): string {
  return `<Project><ItemGroup><PackageVersion Include="${name}" Version="${version}" /></ItemGroup>${extra}</Project>`;
}

test('existing test-tool versions can change', () => {
  for (const name of TEST_TOOLS) {
    assert.deepEqual(
      validateChanges({
        [CENTRAL_PACKAGES]: [packages(name), packages(name, '2.0.0')]
      }),
      []
    );
  }
});

test('runtime, generators and unknown dependencies require manual maintenance', () => {
  for (const name of [
    'Newtonsoft.Json',
    'MessagePackAnalyzer',
    'BepInEx.Core',
    'UnityEngine.Modules',
    'Krafs.Publicizer',
    'Microsoft.Data.Sqlite',
    'Future.Runtime.Library'
  ]) {
    const problems = validateChanges({
      [CENTRAL_PACKAGES]: [packages(name), packages(name, '2.0.0')]
    });
    assert.ok(problems.length > 0, name);
  }
});

test('allowed update cannot hide a new reference or MSBuild target', () => {
  const before = packages('xunit');
  for (const extra of [
    '<ItemGroup><PackageVersion Include="MessagePack" Version="9.0.0" /></ItemGroup>',
    '<Target Name="NewTarget" BeforeTargets="Build" />'
  ]) {
    const problems = validateChanges({
      [CENTRAL_PACKAGES]: [before, packages('xunit', '2.0.0', extra)]
    });
    assert.ok(problems.length > 0, extra);
  }
});

test('runtime lock churn is rejected even with an allowed direct update', () => {
  const problems = validateChanges({
    [CENTRAL_PACKAGES]: [packages('xunit'), packages('xunit', '2.0.0')],
    'bazaarplusplus-mod/src/BazaarPlusPlus/packages.lock.json': ['old', 'new']
  });
  assert.ok(problems.length > 0);
});

test('changes to game references, sources or removed packages are rejected', () => {
  for (const filePath of [
    'bazaarplusplus-mod/build/GameLibraries.props',
    'bazaarplusplus-mod/src/New.cs',
    CENTRAL_PACKAGES
  ]) {
    const problems = validateChanges({ [filePath]: ['old', null] });
    assert.ok(problems.length > 0, filePath);
  }
});

test('invalid XML is rejected', () => {
  for (const broken of [
    '<broken',
    '<Project><ItemGroup></Project>',
    '<Project><PackageVersion Include="xunit" Include="xunit" /></Project>',
    '<Project><ItemGroup /><!-- unclosed',
    '<Project></Project>trailing'
  ]) {
    const problems = validateChanges({
      [CENTRAL_PACKAGES]: [packages('xunit'), broken]
    });
    assert.ok(problems.length > 0, broken);
  }
});

test('real Directory.Packages.props accepts a test-tool bump and rejects a runtime bump', () => {
  const repoRoot = path.resolve(import.meta.dirname, '../..');
  const current = fs.readFileSync(
    path.join(repoRoot, CENTRAL_PACKAGES),
    'utf8'
  );
  const bumpedTest = current.replace(
    /(<PackageVersion Include="xunit" Version=")([^"]+)(")/,
    '$19.9.9$3'
  );
  assert.notEqual(bumpedTest, current);
  assert.deepEqual(
    validateChanges({ [CENTRAL_PACKAGES]: [current, bumpedTest] }),
    []
  );
  const bumpedRuntime = current.replace(
    /(<PackageVersion Include="BepInEx\.Core" Version=")([^"]+)(")/,
    '$19.9.9$3'
  );
  assert.notEqual(bumpedRuntime, current);
  assert.ok(
    validateChanges({ [CENTRAL_PACKAGES]: [current, bumpedRuntime] }).length > 0
  );
});

test('other projects are not subject to mod policy', () => {
  assert.deepEqual(
    validateChanges({ 'bazaarplusplus-server/bun.lock': ['old', 'new'] }),
    []
  );
});

test('CLI runs standalone from a temp dir, ignores base-branch drift and rejects bot lock changes', (t: TestContext) => {
  const scriptSource = path.join(
    import.meta.dirname,
    'check-mod-dependency-update.ts'
  );
  const tempRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-mod-dep-boundary-')
  );
  t.onTestFinished(() => fs.rmSync(tempRoot, { recursive: true, force: true }));
  // Copy the script alone into tempRoot to mirror checks.yml extracting from BASE_SHA into RUNNER_TEMP.
  const standaloneScript = path.join(
    tempRoot,
    'check-mod-dependency-update.ts'
  );
  fs.copyFileSync(scriptSource, standaloneScript);
  const repo = path.join(tempRoot, 'repo');
  fs.mkdirSync(repo);

  const git = (...args: string[]): string =>
    runFixtureGit(args, { cwd: repo }).trim();
  const commit = (): string => {
    git('add', '.');
    git(
      '-c',
      'user.name=Test',
      '-c',
      'user.email=test@example.invalid',
      '-c',
      'commit.gpgsign=false',
      '-c',
      'core.hooksPath=/dev/null',
      'commit',
      '-m',
      'fixture'
    );
    return git('rev-parse', 'HEAD');
  };

  git('init');
  const central = path.join(repo, CENTRAL_PACKAGES);
  fs.mkdirSync(path.dirname(central), { recursive: true });
  fs.writeFileSync(central, packages('xunit'));
  const lock = path.join(
    repo,
    'bazaarplusplus-mod/src/BazaarPlusPlus/packages.lock.json'
  );
  fs.mkdirSync(path.dirname(lock), { recursive: true });
  fs.writeFileSync(lock, 'old lock');
  const base = commit();

  fs.writeFileSync(central, packages('xunit', '2.0.0'));
  const bot = commit();

  git('checkout', '--detach', base);
  fs.writeFileSync(lock, 'base branch moved independently');
  const advancedBase = commit();

  const check = (head: string) =>
    spawnSync(process.execPath, [standaloneScript, advancedBase, head], {
      cwd: repo,
      encoding: 'utf8'
    });

  assert.equal(check(bot).status, 0);
  git('checkout', '--detach', bot);
  fs.writeFileSync(lock, 'bot changed runtime dependency');
  const rejected = check(commit());
  assert.notEqual(rejected.status, 0);
  assert.match(rejected.stderr, /packages\.lock\.json/);
  assert.notEqual(check('missing-revision').status, 0);
});
