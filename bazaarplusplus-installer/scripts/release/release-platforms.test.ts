import { test, expect } from 'vite-plus/test';
import {
  RELEASE_PLATFORMS,
  replayRecorderBundlePath,
  resolveBuildPlatform,
  defaultTargetBuildPlatforms,
  tauriPlatform
} from '../../../release/release-platforms.ts';
import { resolveTargetPlatforms } from '../checks/prebuild-check.ts';

const TAURI_PLATFORMS = RELEASE_PLATFORMS.filter(
  (platform) => platform.kind === 'tauri'
);

test.each(TAURI_PLATFORMS)(
  'tauriPlatform facts for $buildPlatform come from the module',
  (p) => {
    expect(tauriPlatform(p.buildPlatform)).toEqual(p);
    if (p.buildPlatform === 'macos') {
      expect(replayRecorderBundlePath(p.buildPlatform)).toMatch(
        /GfxPluginBppReplayVideoToolbox\.bundle$/
      );
    }
  }
);

test('prebuild-check target platforms derive from the table', () => {
  expect(resolveTargetPlatforms(undefined)).toEqual(
    defaultTargetBuildPlatforms()
  );
  expect(defaultTargetBuildPlatforms()).toEqual(['macos', 'windows']);
  for (const p of TAURI_PLATFORMS) {
    expect(resolveBuildPlatform(p.nodePlatform)).toBe(p.buildPlatform);
  }
});

test.each(['linux', 'android'])(
  'tauriPlatform rejects %s, which has no Tauri installer',
  (platform) => {
    expect(() => tauriPlatform(platform)).toThrow();
  }
);
