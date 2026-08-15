import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import {
  NATIVE_RECORDER_ARTIFACTS,
  computePlatformRequirement,
  nativeRecorderStatus,
  promoteNativeRecorderInput,
  verifyNativeRecorderInput
} from './native-recorder-input.mjs';

function writeFile(rootDir, relativePath, content) {
  const filePath = path.join(rootDir, ...relativePath.split('/'));
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, content);
  return filePath;
}

function sha256(filePath) {
  return crypto
    .createHash('sha256')
    .update(fs.readFileSync(filePath))
    .digest('hex');
}

function testCatalog() {
  return {
    schemaVersion: 1,
    platforms: {
      macos: {
        architecture: 'arm64',
        deploymentTarget: '12.0',
        signing: 'adhoc',
        inputs: ['native/macos-input.c', 'managed/macos-interop.cs'],
        artifacts: [
          {
            ...NATIVE_RECORDER_ARTIFACTS.macos[0],
            buildScript: 'native/build-audio.sh',
            outputPath: 'libBppMacAudio.dylib',
            binaryPath: 'libBppMacAudio.dylib',
            requiredExports: ['BppMacAudio_IsSupported']
          },
          {
            ...NATIVE_RECORDER_ARTIFACTS.macos[1],
            buildScript: 'native/build-replay.sh',
            outputPath: 'GfxPluginBppReplayVideoToolbox.bundle',
            binaryPath:
              'GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox',
            requiredExports: ['UnityPluginLoad']
          }
        ]
      },
      windows: {
        architecture: 'x64',
        signing: 'unsigned',
        inputs: ['native/windows-input.cpp', 'managed/windows-interop.cs'],
        artifacts: [
          {
            ...NATIVE_RECORDER_ARTIFACTS.windows[0],
            buildScript: 'native/build-windows.ps1',
            outputPath: 'GfxPluginBppReplayMediaFoundation.dll',
            binaryPath: 'GfxPluginBppReplayMediaFoundation.dll',
            requiredExports: ['UnityPluginLoad']
          }
        ]
      }
    }
  };
}

function createFixture() {
  const fixtureRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-native-recorder-')
  );
  const rootDir = path.join(fixtureRoot, 'installer');
  const sourceRoot = path.join(fixtureRoot, 'mod');
  const buildRoot = path.join(fixtureRoot, 'build-output');
  fs.mkdirSync(rootDir, { recursive: true });
  writeFile(
    sourceRoot,
    'native/artifacts.json',
    `${JSON.stringify(testCatalog(), null, 2)}\n`
  );
  writeFile(sourceRoot, 'native/macos-input.c', 'mac native input');
  writeFile(sourceRoot, 'managed/macos-interop.cs', 'mac managed ABI');
  writeFile(sourceRoot, 'native/windows-input.cpp', 'windows native input');
  writeFile(sourceRoot, 'managed/windows-interop.cs', 'windows managed ABI');

  writeFile(buildRoot, 'mac-audio/libBppMacAudio.dylib', 'promoted mac audio');
  writeFile(
    buildRoot,
    'mac-replay/GfxPluginBppReplayVideoToolbox.bundle/Contents/Info.plist',
    'promoted info'
  );
  writeFile(
    buildRoot,
    'mac-replay/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox',
    'promoted replay binary'
  );

  execFileSync('git', ['init', '-q'], { cwd: sourceRoot });
  execFileSync('git', ['config', 'user.name', 'Native Test'], {
    cwd: sourceRoot
  });
  execFileSync('git', ['config', 'user.email', 'native@example.test'], {
    cwd: sourceRoot
  });
  execFileSync('git', ['add', '.'], { cwd: sourceRoot });
  execFileSync('git', ['commit', '-qm', 'test inputs'], { cwd: sourceRoot });
  return { fixtureRoot, rootDir, sourceRoot, buildRoot };
}

function removeFixture(fixture) {
  fs.rmSync(fixture.fixtureRoot, { recursive: true, force: true });
}

test('canonical freshness follows declared worktree bytes, not Git HEAD', () => {
  const fixture = createFixture();
  try {
    const commitBefore = execFileSync('git', ['rev-parse', 'HEAD'], {
      cwd: fixture.sourceRoot,
      encoding: 'utf8'
    });
    const before = computePlatformRequirement({
      sourceRoot: fixture.sourceRoot,
      platform: 'macos'
    });
    writeFile(fixture.sourceRoot, 'README.md', 'unrelated worktree change');
    const unrelated = computePlatformRequirement({
      sourceRoot: fixture.sourceRoot,
      platform: 'macos'
    });
    fs.appendFileSync(
      path.join(fixture.sourceRoot, 'managed', 'macos-interop.cs'),
      '\nchanged ABI'
    );
    const after = computePlatformRequirement({
      sourceRoot: fixture.sourceRoot,
      platform: 'macos'
    });
    const commitAfter = execFileSync('git', ['rev-parse', 'HEAD'], {
      cwd: fixture.sourceRoot,
      encoding: 'utf8'
    });

    expect(unrelated.inputDigest).toBe(before.inputDigest);
    expect(after.inputDigest).not.toBe(before.inputDigest);
    expect(commitAfter).toBe(commitBefore);
  } finally {
    removeFixture(fixture);
  }
});

test('output-affecting catalog policy participates in freshness', () => {
  const fixture = createFixture();
  try {
    const before = computePlatformRequirement({
      sourceRoot: fixture.sourceRoot,
      platform: 'macos'
    });
    const catalogPath = path.join(
      fixture.sourceRoot,
      'native',
      'artifacts.json'
    );
    const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
    catalog.platforms.macos.deploymentTarget = '13.0';
    fs.writeFileSync(catalogPath, `${JSON.stringify(catalog, null, 2)}\n`);
    const after = computePlatformRequirement({
      sourceRoot: fixture.sourceRoot,
      platform: 'macos'
    });
    expect(after.inputDigest).not.toBe(before.inputDigest);
  } finally {
    removeFixture(fixture);
  }
});

test('promotion rejects source drift that occurs during a producer build', () => {
  const fixture = createFixture();
  try {
    const beforeBuild = computePlatformRequirement({
      sourceRoot: fixture.sourceRoot,
      platform: 'macos'
    });
    fs.appendFileSync(
      path.join(fixture.sourceRoot, 'native', 'macos-input.c'),
      '\nchanged during build'
    );

    expect(() =>
      promoteNativeRecorderInput({
        ...fixture,
        platform: 'macos',
        expectedInputDigest: beforeBuild.inputDigest
      })
    ).toThrow('inputs changed while the producer build was running');
    expect(
      fs.existsSync(
        path.join(
          fixture.rootDir,
          'scripts/release/native-recorder-input.lock.json'
        )
      )
    ).toBe(false);
    expect(
      fs.existsSync(
        path.join(
          fixture.rootDir,
          NATIVE_RECORDER_ARTIFACTS.macos[0].destinationPath
        )
      )
    ).toBe(false);
  } finally {
    removeFixture(fixture);
  }
});

test('local promotion writes schema 3 provenance and exact artifact trees', () => {
  const fixture = createFixture();
  try {
    promoteNativeRecorderInput({
      ...fixture,
      platform: 'macos'
    });

    const result = verifyNativeRecorderInput({
      rootDir: fixture.rootDir,
      platforms: ['macos']
    });
    const manifest = JSON.parse(
      fs.readFileSync(
        path.join(
          fixture.rootDir,
          'scripts/release/native-recorder-input.lock.json'
        ),
        'utf8'
      )
    );
    expect(result.platforms).toEqual(['macos']);
    expect(manifest).toMatchObject({
      schemaVersion: 3,
      sourceRepository: 'BazaarPlusPlus/bazaarplusplus-mod',
      platforms: {
        macos: {
          inputDigest: expect.stringMatching(/^sha256:[0-9a-f]{64}$/),
          policy: {
            architecture: 'arm64',
            deploymentTarget: '12.0',
            signing: 'adhoc'
          },
          producer: {
            repository: 'BazaarPlusPlus/bazaarplusplus-mod',
            commit: expect.stringMatching(/^[0-9a-f]{40}$/),
            dirty: false
          }
        }
      }
    });
  } finally {
    removeFixture(fixture);
  }
});

test('freshness rejects changed inputs and additions to a promoted bundle tree', () => {
  const fixture = createFixture();
  try {
    promoteNativeRecorderInput({ ...fixture, platform: 'macos' });
    expect(
      nativeRecorderStatus({
        rootDir: fixture.rootDir,
        sourceRoot: fixture.sourceRoot,
        platform: 'macos'
      })
    ).toMatchObject({ fresh: true });

    fs.appendFileSync(
      path.join(fixture.sourceRoot, 'native', 'macos-input.c'),
      '\nsource drift'
    );
    expect(
      nativeRecorderStatus({
        rootDir: fixture.rootDir,
        sourceRoot: fixture.sourceRoot,
        platform: 'macos'
      })
    ).toMatchObject({ fresh: false, reason: 'canonical input digest changed' });

    fs.writeFileSync(
      path.join(fixture.sourceRoot, 'native', 'macos-input.c'),
      'mac native input'
    );
    writeFile(
      fixture.rootDir,
      `${NATIVE_RECORDER_ARTIFACTS.macos[1].destinationPath}/unexpected.bin`,
      'unexpected'
    );
    expect(
      nativeRecorderStatus({
        rootDir: fixture.rootDir,
        sourceRoot: fixture.sourceRoot,
        platform: 'macos'
      })
    ).toMatchObject({ fresh: false });
  } finally {
    removeFixture(fixture);
  }
});

test('an integrity-pinned platform without an input digest remains stale', () => {
  const fixture = createFixture();
  try {
    promoteNativeRecorderInput({ ...fixture, platform: 'macos' });
    const windowsArtifact = NATIVE_RECORDER_ARTIFACTS.windows[0];
    const windowsPath = writeFile(
      fixture.rootDir,
      windowsArtifact.destinationPath,
      'integrity-pinned windows input'
    );
    const lockPath = path.join(
      fixture.rootDir,
      'scripts/release/native-recorder-input.lock.json'
    );
    const manifest = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
    manifest.platforms.windows = {
      inputDigest: null,
      inputFiles: {},
      policy: { architecture: 'x64', signing: 'unsigned' },
      producer: {
        repository: 'BazaarPlusPlus/bazaarplusplus-mod',
        commit: '1'.repeat(40),
        dirty: null
      },
      artifacts: {
        'windows-replay': {
          destinationPath: windowsArtifact.destinationPath,
          content: {
            kind: 'file',
            size: fs.statSync(windowsPath).size,
            sha256: sha256(windowsPath)
          }
        }
      }
    };
    fs.writeFileSync(lockPath, `${JSON.stringify(manifest, null, 2)}\n`);

    expect(manifest.platforms.windows).toMatchObject({
      inputDigest: null,
      policy: { architecture: 'x64', signing: 'unsigned' }
    });
    expect(() =>
      verifyNativeRecorderInput({
        rootDir: fixture.rootDir,
        platforms: ['windows']
      })
    ).not.toThrow();
    expect(
      nativeRecorderStatus({
        rootDir: fixture.rootDir,
        sourceRoot: fixture.sourceRoot,
        platform: 'windows'
      })
    ).toMatchObject({ fresh: false, reason: 'canonical input digest changed' });
  } finally {
    removeFixture(fixture);
  }
});
