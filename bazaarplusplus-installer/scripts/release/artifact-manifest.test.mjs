import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import {
  createArtifactManifest,
  validateArtifactManifest
} from './artifact-manifest.mjs';

function windowsFixture() {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-artifacts-'));
  const bundleDir = path.join(
    rootDir,
    'src-tauri',
    'target',
    'release',
    'bundle',
    'nsis'
  );
  fs.mkdirSync(bundleDir, { recursive: true });
  const installer = path.join(bundleDir, 'BazaarPlusPlus_9.9.9_x64-setup.exe');
  fs.writeFileSync(installer, 'installer bytes');
  fs.writeFileSync(`${installer}.sig`, 'public-signature\n');
  return { rootDir, bundleDir, installer, signature: `${installer}.sig` };
}

function macosFixture() {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-artifacts-'));
  const bundleRoot = path.join(
    rootDir,
    'src-tauri',
    'target',
    'aarch64-apple-darwin',
    'release',
    'bundle'
  );
  const installerDir = path.join(bundleRoot, 'dmg');
  const updaterDir = path.join(bundleRoot, 'macos');
  fs.mkdirSync(installerDir, { recursive: true });
  fs.mkdirSync(updaterDir, { recursive: true });
  const installer = path.join(installerDir, 'BazaarPlusPlus_9.9.9_aarch64.dmg');
  const updater = path.join(updaterDir, 'BazaarPlusPlus.app.tar.gz');
  const signature = `${updater}.sig`;
  fs.writeFileSync(installer, 'installer bytes');
  fs.writeFileSync(updater, 'updater bytes');
  fs.writeFileSync(signature, 'public-signature\n');
  return { rootDir, installer, updater, signature };
}

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
      schemaVersion: 1,
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
