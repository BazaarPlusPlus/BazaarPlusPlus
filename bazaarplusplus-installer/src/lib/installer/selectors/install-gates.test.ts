import { test, expect } from 'vitest';

import { selectInstallGates } from './install-gates.ts';

test('selectInstallGates trims custom paths and exposes install state', () => {
  const selection = selectInstallGates({
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
    isDebugInstallPreview: false
  });

  expect(selection.selectedPath).toBe('D:\\Bazaar Custom');
  expect(selection.modInstalled).toBe(true);
  expect(selection.hasPath).toBe(true);
  expect(selection.canInstall).toBe(true);
  expect(selection.canLaunchGame).toBe(true);
  expect(selection.versionMismatch).toBe(true);
});

test('selectInstallGates reports no path when neither detected nor custom paths exist', () => {
  const selection = selectInstallGates({
    env: null,
    bazaarFound: false,
    customGamePath: '   ',
    cachedDetectedGamePath: '',
    actionBusy: 'idle',
    isDebugInstallPreview: false
  });

  expect(selection.selectedPath).toBe(null);
  expect(selection.hasPath).toBe(false);
  expect(selection.pageState.effectiveGamePath).toBe('');
  expect(selection.canInstall).toBe(false);
  expect(selection.canLaunchGame).toBe(false);
});

test('selectInstallGates reflects busy actions through pageState', () => {
  const selection = selectInstallGates({
    env: {
      steam_path: 'C:\\Program Files (x86)\\Steam',
      steam_launch_options_supported: true,
      game_path: 'C:\\Games\\The Bazaar',
      game_path_valid: true,
      dotnet_version: '9.0.1',
      dotnet_ok: true,
      bepinex_installed: false,
      bpp_version: null,
      bundled_bpp_version: '3.1.0'
    },
    bazaarFound: true,
    customGamePath: '',
    cachedDetectedGamePath: '',
    actionBusy: 'install',
    isDebugInstallPreview: false
  });

  expect(selection.isBusy).toBe(true);
  expect(selection.canInstall).toBe(false);
  expect(selection.canLaunchGame).toBe(false);
});

test('selectInstallGates uses the cached detected game path until fresh env data arrives', () => {
  const selection = selectInstallGates({
    env: null,
    bazaarFound: false,
    customGamePath: '',
    cachedDetectedGamePath: '  C:\\Games\\The Bazaar  ',
    actionBusy: 'idle',
    isDebugInstallPreview: false
  });

  expect(selection.selectedPath).toBe(null);
  expect(selection.hasPath).toBe(true);
  expect(selection.pageState.effectiveGamePath).toBe('C:\\Games\\The Bazaar');
  expect(selection.canInstall).toBe(false);
});

test('selectInstallGates prefers the custom game path over a cached detected one', () => {
  const selection = selectInstallGates({
    env: null,
    bazaarFound: false,
    customGamePath: '  D:\\Bazaar Custom  ',
    cachedDetectedGamePath: 'C:\\Games\\The Bazaar',
    actionBusy: 'idle',
    isDebugInstallPreview: false
  });

  expect(selection.selectedPath).toBe('D:\\Bazaar Custom');
  expect(selection.pageState.effectiveGamePath).toBe('D:\\Bazaar Custom');
});

test('selectInstallGates prefers the freshly-detected env path over any cached value', () => {
  const selection = selectInstallGates({
    env: {
      steam_path: 'C:\\Program Files (x86)\\Steam',
      steam_launch_options_supported: true,
      game_path: 'C:\\Games\\The Bazaar Fresh',
      game_path_valid: true,
      dotnet_version: '9.0.1',
      dotnet_ok: true,
      bepinex_installed: true,
      bpp_version: null,
      bundled_bpp_version: '3.1.0'
    },
    bazaarFound: true,
    customGamePath: '',
    cachedDetectedGamePath: 'C:\\Games\\The Bazaar Stale',
    actionBusy: 'idle',
    isDebugInstallPreview: false
  });

  expect(selection.pageState.effectiveGamePath).toBe('C:\\Games\\The Bazaar Fresh');
});
