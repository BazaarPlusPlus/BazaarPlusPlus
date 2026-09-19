import { test, expect } from 'vitest';
import path from 'node:path';
import { runShell } from '../test-support/shell.mjs';
import {
  RELEASE_PLATFORMS,
  resolveBuildPlatform,
  defaultTargetBuildPlatforms
} from '../../../release/release-platforms.mjs';
import { resolveBundleCleanupPath } from './before-bundle-cleanup.mjs';
import { resolveTargetPlatforms } from '../checks/prebuild-check.mjs';

test.each(RELEASE_PLATFORMS)(
  'bundle.sh facts for $buildPlatform come from the module',
  (p) => {
    const out = runShell(`
      set -euo pipefail
      source ./scripts/bundle.sh
      printf 'r2key=%s\\n' "$(release_platforms_cli r2-key ${p.buildPlatform})"
      printf 'bundleroot=%s\\n' "$(release_platforms_cli bundle-root ${p.buildPlatform})"
      printf 'rust=[%s]\\n' "$(release_platforms_cli rust-targets ${p.buildPlatform})"
    `);
    expect(out).toContain(`r2key=${p.key}`);
    expect(out).toContain(`bundleroot=${p.bundleRoot}`);
    expect(out).toContain(`rust=[${p.rustTarget ?? ''}]`);
  }
);

test.each(RELEASE_PLATFORMS)(
  'build_prod $buildPlatform uses derived paths/targets/bundles',
  (p) => {
    const out = runShell(`
      set -euo pipefail
      source ./scripts/bundle.sh
      assert_file() { :; }
      prepare_signed_macos_resource_zip() { :; }
      prepare_signed_macos_resource_binary() { :; }
      invoke_step() { local l="$1"; shift; printf '%s|%s\\n' "$l" "$*"; }
      build_prod ${p.buildPlatform}
    `);
    if (p.rustTarget) expect(out).toContain(`--target ${p.rustTarget}`);
    else expect(out).not.toContain('--target');
    expect(out).toContain(`--bundles ${p.bundleTargets}`);
    expect(out).toMatch(new RegExp(`Binary:\\s+.*/${p.releaseBinary}\\n`));
    expect(out).toMatch(new RegExp(`Bundle:\\s+.*/${p.installerDir}\\n`));
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
