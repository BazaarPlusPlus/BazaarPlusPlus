import { test, expect } from 'vitest';
import path from 'node:path';
import { runShell } from '../test-support/shell.mjs';
import {
  RELEASE_PLATFORMS,
  replayRecorderBundlePath,
  resolveBuildPlatform,
  defaultTargetBuildPlatforms
} from '../../../release/release-platforms.mjs';
import { resolveBundleCleanupPath } from './before-bundle-cleanup.mjs';
import { resolveTargetPlatforms } from '../checks/prebuild-check.mjs';

test.each(RELEASE_PLATFORMS)(
  'bundle.sh facts for $buildPlatform come from the module',
  (p) => {
    const out = runShell(
      `
      set -euo pipefail
      source ./scripts/bundle.sh
      printf 'r2key=%s\\n' "$(release_platforms_cli r2-key "$BPP_TEST_BUILD_PLATFORM")"
      printf 'bundleroot=%s\\n' "$(release_platforms_cli bundle-root "$BPP_TEST_BUILD_PLATFORM")"
      printf 'rust=[%s]\\n' "$(release_platforms_cli rust-targets "$BPP_TEST_BUILD_PLATFORM")"
      # Only the macOS Payload carries a replay recorder plugin bundle to sign.
      if [ "$BPP_TEST_BUILD_PLATFORM" = macos ]; then
        printf 'replay=%s\\n' "$(release_platforms_cli replay-recorder-bundle "$BPP_TEST_BUILD_PLATFORM")"
      fi
    `,
      { BPP_TEST_BUILD_PLATFORM: p.buildPlatform }
    );
    expect(out).toContain(`r2key=${p.key}`);
    expect(out).toContain(`bundleroot=${p.bundleRoot}`);
    expect(out).toContain(`rust=[${p.rustTarget ?? ''}]`);
    if (p.buildPlatform === 'macos') {
      expect(out).toContain(
        `replay=${replayRecorderBundlePath(p.buildPlatform)}`
      );
    }
  }
);

test.each(RELEASE_PLATFORMS)(
  'before-bundle-cleanup dir for $key matches the module',
  (p) => {
    const expected = path.join('/root', ...p.bundleCleanupDir.split('/'));
    expect(resolveBundleCleanupPath('/root', p.buildPlatform)).toBe(expected);
    expect(resolveBundleCleanupPath('/root', p.nodePlatform)).toBe(expected);
  }
);

test('prebuild-check target platforms derive from the table', () => {
  expect(resolveTargetPlatforms(undefined)).toEqual(
    defaultTargetBuildPlatforms()
  );
  expect(defaultTargetBuildPlatforms()).toEqual(['macos', 'windows']);
  for (const p of RELEASE_PLATFORMS) {
    expect(resolveBuildPlatform(p.nodePlatform)).toBe(p.buildPlatform);
  }
});

test('r2-key rejects an unsupported platform', () => {
  const out = runShell(`
    source ./scripts/bundle.sh
    set +e
    release_platforms_cli r2-key linux 2>&1
    printf 'exit:%s\\n' "$?"
  `);
  expect(out).toContain('exit:1');
});
