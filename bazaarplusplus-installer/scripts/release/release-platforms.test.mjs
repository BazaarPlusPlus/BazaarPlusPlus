import { test, expect } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { runShell, toBashPath } from '../test-support/shell.mjs';
import {
  RELEASE_PLATFORMS,
  RELEASE_PLATFORM_KEYS,
  bundleRoot,
  installerDir,
  bundleCleanupDir,
  releaseBinary,
  updaterFragmentUrl,
  resolveBuildPlatform,
  defaultTargetBuildPlatforms
} from './release-platforms.mjs';
import { resolveBundleCleanupPath } from './before-bundle-cleanup.mjs';
import { resolveTargetPlatforms } from '../checks/prebuild-check.mjs';

test.each(RELEASE_PLATFORMS)(
  'Tauri overlay and target layout agree with $key',
  (platform) => {
    const overlay = JSON.parse(fs.readFileSync(platform.tauriConfig, 'utf8'));
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

test.each(RELEASE_PLATFORMS)(
  'build.sh facts for $buildPlatform come from the module',
  (p) => {
    const out = runShell(`
      set -euo pipefail
      source ./build.sh
      printf 'r2key=%s\\n' "$(platform_r2_key ${p.buildPlatform})"
      printf 'bundleroot=%s\\n' "$(bundle_root_for_platform ${p.buildPlatform})"
      printf 'instdir=%s\\n' "$(release_platforms_cli installer-dir ${p.buildPlatform})"
      printf 'glob=%s\\n' "$(release_platforms_cli installer-glob ${p.buildPlatform})"
      printf 'rust=[%s]\\n' "$(required_rust_targets_for_platform ${p.buildPlatform})"
    `);
    expect(out).toContain(`r2key=${p.key}`);
    expect(out).toMatch(new RegExp(`bundleroot=.*/${bundleRoot(p)}\\n`));
    expect(out).toContain(`instdir=${installerDir(p)}`);
    expect(out).toContain(`glob=${p.installerNameGlob}`);
    expect(out).toContain(`rust=[${p.rustTarget ?? ''}]`);
  }
);

test.each(RELEASE_PLATFORMS)(
  'build_prod $buildPlatform uses derived paths/targets/bundles',
  (p) => {
    const out = runShell(`
      set -euo pipefail
      source ./build.sh
      assert_file() { :; }
      prepare_signed_macos_resource_zip() { :; }
      prepare_signed_macos_resource_binary() { :; }
      invoke_step() { local l="$1"; shift; printf '%s|%s\\n' "$l" "$*"; }
      build_prod ${p.buildPlatform}
    `);
    if (p.rustTarget) expect(out).toContain(`--target ${p.rustTarget}`);
    else expect(out).not.toContain('--target');
    expect(out).toContain(`--bundles ${p.bundleTargets}`);
    expect(out).toMatch(new RegExp(`Binary:\\s+.*/${releaseBinary(p)}\\n`));
    expect(out).toMatch(new RegExp(`Bundle:\\s+.*/${installerDir(p)}\\n`));
  }
);

test.each(RELEASE_PLATFORMS)(
  'before-bundle-cleanup dir for $key matches the module',
  (p) => {
    const expected = path.join('/root', ...bundleCleanupDir(p).split('/'));
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

test('generate_latest_manifest end-to-end emits every table platform in order', () => {
  const outDir = fs.mkdtempSync(`${process.cwd()}/.bpp-latest-e2e-`);
  const outDirBash = toBashPath(outDir);
  try {
    runShell(`
      set -euo pipefail
      source ./build.sh
      wrangler_cli() {
        local key="$4" file="$6"
        case "$key" in
          "$R2_BUCKET"/9.9.9/*/updater/platform-manifest.json)
            local pk="\${key#$R2_BUCKET/9.9.9/}"; pk="\${pk%%/*}"
            printf '{"version":"9.9.9","platform":"%s","url":"https://base/9.9.9/%s/updater/a.bin","signature":"sig"}' "$pk" "$pk" > "$file"
            ;;
          *) return 1 ;;
        esac
      }
      upload_r2_object() { cp "$1" "${outDirBash}/uploaded-$2"; }
      generate_latest_manifest 9.9.9
    `);
    const latest = JSON.parse(
      fs.readFileSync(`${outDir}/uploaded-latest.json`, 'utf8')
    );
    expect(Object.keys(latest.platforms)).toEqual(RELEASE_PLATFORM_KEYS);
    for (const key of RELEASE_PLATFORM_KEYS) {
      expect(latest.platforms[key].url).toBe(
        updaterFragmentUrl({
          baseUrl: 'https://base',
          version: '9.9.9',
          platformKey: key,
          updaterFileName: 'a.bin'
        })
      );
    }
  } finally {
    fs.rmSync(outDir, { recursive: true, force: true });
  }
});

test('r2-key unknown platform keeps the exact build.sh error contract', () => {
  const out = runShell(`
    source ./build.sh
    set +e
    platform_r2_key linux 2>&1
    printf 'exit:%s\\n' "$?"
  `);
  expect(out).toContain('Error: Unsupported platform for upload: linux');
  expect(out).toContain('exit:1');
});

test('CLI list flushes full stdout with exit 0; unknown verb exits 1', () => {
  const out = execFileSync(
    process.execPath,
    ['scripts/release/release-platforms.mjs', 'list'],
    { cwd: process.cwd() }
  );
  expect(out.toString()).toBe(RELEASE_PLATFORM_KEYS.join('\n') + '\n');
  expect(() =>
    execFileSync(
      process.execPath,
      ['scripts/release/release-platforms.mjs', 'bogus'],
      {
        cwd: process.cwd()
      }
    )
  ).toThrow();
});
