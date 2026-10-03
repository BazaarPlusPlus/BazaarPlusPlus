import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import {
  createArtifactManifest,
  gitStateForRoot,
  validateArtifactManifest
} from './artifact-manifest.mjs';
import { runFixtureGit } from '../scripts/test-support/git-fixture.mjs';
import { artifactFixture } from './test-support/artifact-fixture.mjs';

const windowsFixture = () => artifactFixture('windows');
const macosFixture = () => artifactFixture('macos');

const cleanGit = { commit: 'a'.repeat(40), dirty: false };

test('successful build records exact artifacts, hashes, signature, and provenance', () => {
  const fixture = windowsFixture();
  try {
    const { manifest, manifestPath } = createArtifactManifest({
      rootDir: fixture.rootDir,
      platform: 'windows',
      version: '9.9.9',
      gitState: cleanGit,
      builtAt: new Date('2026-07-18T12:00:00.000Z')
    });

    expect(manifest).toMatchObject({
      schemaVersion: 2,
      appVersion: '9.9.9',
      buildPlatform: 'windows',
      releasePlatformKey: 'windows-x86_64',
      gitCommit: cleanGit.commit,
      dirty: false,
      builtAt: '2026-07-18T12:00:00.000Z',
      signature: { content: 'public-signature' }
    });
    expect(manifest.installer.path).toMatch(/BazaarPlusPlus_9\.9\.9/);
    expect(manifest.updater.path).toBe(manifest.installer.path);
    expect(manifest.installer.size).toBe('installer bytes'.length);
    expect(manifest.installer.sha256).toMatch(/^[a-f0-9]{64}$/);
    expect(fs.existsSync(manifestPath)).toBe(true);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});

test('macOS manifest accepts Tauri updater names without an embedded version', () => {
  const fixture = macosFixture();
  try {
    const { manifest, manifestPath } = createArtifactManifest({
      rootDir: fixture.rootDir,
      platform: 'macos',
      version: '9.9.9',
      gitState: cleanGit
    });

    expect(manifest.installer.path).toMatch(/BazaarPlusPlus_9\.9\.9/);
    expect(manifest.updater.path).toMatch(/BazaarPlusPlus\.app\.tar\.gz$/);
    expect(
      validateArtifactManifest({
        rootDir: fixture.rootDir,
        manifestPath,
        platform: 'macos',
        version: '9.9.9',
        gitState: cleanGit
      }).updater
    ).toBe(fixture.updater);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});

test('manifest validation detects artifact tampering and missing files', () => {
  const fixture = windowsFixture();
  try {
    const { manifestPath } = createArtifactManifest({
      rootDir: fixture.rootDir,
      platform: 'windows',
      version: '9.9.9',
      gitState: cleanGit
    });
    fs.appendFileSync(fixture.installer, 'tampered');
    expect(() =>
      validateArtifactManifest({
        rootDir: fixture.rootDir,
        manifestPath,
        platform: 'windows',
        version: '9.9.9',
        gitState: cleanGit
      })
    ).toThrow(/size or SHA-256/i);

    fs.rmSync(fixture.installer);
    expect(() =>
      validateArtifactManifest({
        rootDir: fixture.rootDir,
        manifestPath,
        platform: 'windows',
        version: '9.9.9',
        gitState: cleanGit
      })
    ).toThrow(/missing artifact/i);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});

test('upload validation fails when no artifact manifest exists', () => {
  const fixture = windowsFixture();
  try {
    expect(() =>
      validateArtifactManifest({
        rootDir: fixture.rootDir,
        manifestPath: path.join(fixture.rootDir, 'missing.json'),
        platform: 'windows',
        version: '9.9.9',
        gitState: cleanGit
      })
    ).toThrow(/Missing artifact manifest/);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});

test('manifest generation refuses multiple updater candidates', () => {
  const fixture = windowsFixture();
  const second = path.join(fixture.bundleDir, 'second_9.9.9.tar.gz');
  fs.writeFileSync(second, 'updater');
  fs.writeFileSync(`${second}.sig`, 'sig');
  try {
    expect(() =>
      createArtifactManifest({
        rootDir: fixture.rootDir,
        platform: 'windows',
        version: '9.9.9',
        gitState: cleanGit
      })
    ).toThrow(/Multiple updater signatures/);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});

test.each([
  [
    'old version',
    { version: '10.0.0', gitState: cleanGit },
    /version mismatch/i
  ],
  [
    'old commit',
    { version: '9.9.9', gitState: { commit: 'b'.repeat(40), dirty: false } },
    /commit mismatch/i
  ]
])('manifest validation rejects %s', (_name, validation, error) => {
  const fixture = windowsFixture();
  try {
    const { manifestPath } = createArtifactManifest({
      rootDir: fixture.rootDir,
      platform: 'windows',
      version: '9.9.9',
      gitState: cleanGit
    });
    expect(() =>
      validateArtifactManifest({
        rootDir: fixture.rootDir,
        manifestPath,
        platform: 'windows',
        ...validation
      })
    ).toThrow(error);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});

test('manifest validation rejects a dirty build or dirty current checkout', () => {
  const fixture = windowsFixture();
  try {
    const { manifestPath } = createArtifactManifest({
      rootDir: fixture.rootDir,
      platform: 'windows',
      version: '9.9.9',
      gitState: { ...cleanGit, dirty: true }
    });
    expect(() =>
      validateArtifactManifest({
        rootDir: fixture.rootDir,
        manifestPath,
        platform: 'windows',
        version: '9.9.9',
        gitState: cleanGit
      })
    ).toThrow(/dirty build/i);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});

test('gitStateForRoot ignores siblings but includes the shared release Git helper', () => {
  const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-git-state-'));
  const installerDir = path.join(fixtureRoot, 'bazaarplusplus-installer');
  try {
    fs.mkdirSync(installerDir, { recursive: true });
    fs.writeFileSync(path.join(installerDir, 'README.md'), 'installer\n');
    runFixtureGit(['init', '-q'], { cwd: fixtureRoot });
    runFixtureGit(['config', 'user.name', 'Manifest Test'], {
      cwd: fixtureRoot
    });
    runFixtureGit(['config', 'user.email', 'manifest@example.test'], {
      cwd: fixtureRoot
    });
    runFixtureGit(['add', '.'], { cwd: fixtureRoot });
    runFixtureGit(['commit', '-qm', 'installer snapshot'], {
      cwd: fixtureRoot
    });
    fs.writeFileSync(
      path.join(fixtureRoot, 'sibling-dirty.txt'),
      'other work\n'
    );

    const state = gitStateForRoot(installerDir);
    expect(state.dirty).toBe(false);
    expect(state.commit).toMatch(/^[0-9a-f]{40}$/);
    fs.mkdirSync(path.join(fixtureRoot, 'scripts'));
    fs.writeFileSync(
      path.join(fixtureRoot, 'scripts/git-command.mjs'),
      '// release input\n'
    );
    expect(gitStateForRoot(installerDir).dirty).toBe(true);
  } finally {
    fs.rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test('manifest generation refuses multiple candidate installers', () => {
  const fixture = windowsFixture();
  fs.writeFileSync(
    path.join(fixture.bundleDir, 'BazaarPlusPlus_9.9.9_second.exe'),
    'stale'
  );
  try {
    expect(() =>
      createArtifactManifest({
        rootDir: fixture.rootDir,
        platform: 'windows',
        version: '9.9.9',
        gitState: cleanGit
      })
    ).toThrow(/multiple installer artifacts/i);
  } finally {
    fs.rmSync(fixture.rootDir, { recursive: true, force: true });
  }
});
