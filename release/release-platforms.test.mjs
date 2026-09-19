import { test, expect } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { WORKSPACE_ROOT } from './product.mjs';
import {
  RELEASE_PLATFORMS,
  RELEASE_PLATFORM_KEYS,
  resolveBuildPlatform
} from './release-platforms.mjs';

test.each(RELEASE_PLATFORMS)(
  'Tauri overlay and target layout agree with $key',
  (platform) => {
    const overlay = JSON.parse(
      fs.readFileSync(
        path.join(
          WORKSPACE_ROOT,
          'bazaarplusplus-installer',
          platform.tauriConfig
        ),
        'utf8'
      )
    );
    expect(overlay.bundle.targets).toEqual(platform.bundleTargets.split(','));

    const resourceSource = platform.resourceZip.replace(/^src-tauri\//, '');
    expect(overlay.bundle.resources[resourceSource]).toBe(
      'BepInExSource/BepInEx.zip'
    );

    const releaseRoot = platform.rustTarget
      ? `src-tauri/target/${platform.rustTarget}/release`
      : 'src-tauri/target/release';
    expect(platform.releaseBinary.startsWith(`${releaseRoot}/`)).toBe(true);
    expect(platform.bundleRoot).toBe(`${releaseRoot}/bundle`);
    expect(platform.installerDir.startsWith(`${platform.bundleRoot}/`)).toBe(
      true
    );
    expect(resolveBuildPlatform(platform.buildPlatform)).toBe(
      platform.buildPlatform
    );
  }
);

test('CLI list flushes full stdout with exit 0; unknown verb exits 1', () => {
  const out = execFileSync(
    process.execPath,
    ['release/release-platforms.mjs', 'list'],
    { cwd: process.cwd() }
  );
  expect(out.toString()).toBe(RELEASE_PLATFORM_KEYS.join('\n') + '\n');
  expect(() =>
    execFileSync(process.execPath, ['release/release-platforms.mjs', 'bogus'], {
      cwd: process.cwd()
    })
  ).toThrow();
});
