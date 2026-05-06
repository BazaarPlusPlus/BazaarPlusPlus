import { test, expect } from 'vitest';

import {
  createInstallDebugEnvironment,
  createInstallPageModel
} from './page-model.ts';

function localized(zh: string, en: string): string {
  return en || zh;
}

function t(key: string, params?: Record<string, string | number>): string {
  if (key === 'updaterReady') {
    return `Ready ${params?.version ?? ''}`.trim();
  }

  return key;
}

test('createInstallDebugEnvironment returns a stable preview environment', () => {
  const env = createInstallDebugEnvironment();

  expect(env.game_path).toBe('C:\\Games\\The Bazaar');
  expect(env.dotnet_ok).toBe(true);
  expect(env.bundled_bpp_version).toBe('debug-preview');
});

test('createInstallPageModel centralizes install page derivations', () => {
  const model = createInstallPageModel({
    env: {
      steam_path: 'C:\\Program Files (x86)\\Steam',
      steam_launch_options_supported: true,
      game_path: 'C:\\Games\\The Bazaar',
      game_path_valid: true,
      dotnet_version: '9.0.1',
      dotnet_ok: true,
      bepinex_installed: true,
      bpp_version: '3.0.0',
      bundled_bpp_version: '3.1.0'
    },
    bazaarFound: true,
    customGamePath: '  D:\\Bazaar Custom  ',
    cachedDetectedGamePath: '',
    actionBusy: 'idle',
    showStreamMode: false,
    locale: 'en',
    isDebugInstallPreview: false,
    updaterSnapshot: {
      status: 'available',
      currentVersion: '3.0.0',
      availableVersion: '3.1.0',
      errorMessage: null,
      progress: {
        downloadedBytes: 0,
        totalBytes: null
      }
    },
    hasPendingUpdate: true,
    pendingSteamAction: 'install',
    localized,
    t
  });

  expect(model.selectedPath).toBe('D:\\Bazaar Custom');
  expect(model.canInstall).toBe(true);
  expect(model.versionMismatch).toBe(true);
  expect(model.updaterButtonLabel).toBe('Ready 3.1.0');
  expect(model.steamModalTitle).toBe('installRiskTitle');
});

test('createInstallPageModel hydrates effectiveGamePath from cached detection before env loads', () => {
  const model = createInstallPageModel({
    env: null,
    bazaarFound: false,
    customGamePath: '',
    cachedDetectedGamePath: '  C:\\Games\\The Bazaar  ',
    actionBusy: 'idle',
    showStreamMode: false,
    locale: 'en',
    isDebugInstallPreview: false,
    updaterSnapshot: {
      status: 'idle',
      currentVersion: null,
      availableVersion: null,
      errorMessage: null,
      progress: {
        downloadedBytes: 0,
        totalBytes: null
      }
    },
    hasPendingUpdate: false,
    pendingSteamAction: null,
    localized,
    t
  });

  expect(model.pageState.effectiveGamePath).toBe('C:\\Games\\The Bazaar');
  expect(model.hasPath).toBe(true);
  expect(model.canInstall).toBe(false);
});
