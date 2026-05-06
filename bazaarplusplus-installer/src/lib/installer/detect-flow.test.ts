import { test, expect } from 'vitest';

import { detectInstallerEnvironment } from './detect-flow.ts';

test('detectInstallerEnvironment keeps env null when environment detection fails', async () => {
  const result = await detectInstallerEnvironment({
    requestedGamePath: 'C:\\Games\\The Bazaar',
    detectEnvironment: async () => {
      throw new Error('detect failed');
    }
  });

  expect(result).toEqual({
    env: null,
    dotnetState: 'idle',
    bazaarFound: false,
    bazaarInvalid: false
  });
});

test('detectInstallerEnvironment derives dotnet state from environment response', async () => {
  const calls: string[] = [];

  const result = await detectInstallerEnvironment({
    requestedGamePath: 'D:\\Bazaar',
    detectEnvironment: async (gamePath) => {
      calls.push(`detect:${gamePath ?? ''}`);
      return {
        steam_path: 'C:\\Program Files (x86)\\Steam',
        steam_launch_options_supported: true,
        game_path: gamePath ?? null,
        game_path_valid: true,
        dotnet_version: '9.0.1',
        dotnet_ok: true,
        bepinex_installed: false,
        bpp_version: null,
        bundled_bpp_version: '2.0.0'
      };
    }
  });

  expect(calls).toEqual(['detect:D:\\Bazaar']);
  expect(result).toEqual({
    env: {
      steam_path: 'C:\\Program Files (x86)\\Steam',
      steam_launch_options_supported: true,
      game_path: 'D:\\Bazaar',
      game_path_valid: true,
      dotnet_version: '9.0.1',
      dotnet_ok: true,
      bepinex_installed: false,
      bpp_version: null,
      bundled_bpp_version: '2.0.0'
    },
    dotnetState: 'found',
    bazaarFound: true,
    bazaarInvalid: false
  });
});

test('detectInstallerEnvironment reports not_found dotnet state when runtime missing', async () => {
  const result = await detectInstallerEnvironment({
    requestedGamePath: null,
    detectEnvironment: async () => ({
      steam_path: null,
      steam_launch_options_supported: false,
      game_path: null,
      game_path_valid: false,
      dotnet_version: null,
      dotnet_ok: false,
      bepinex_installed: false,
      bpp_version: null,
      bundled_bpp_version: null
    })
  });

  expect(result.dotnetState).toBe('not_found');
  expect(result.bazaarFound).toBe(false);
  expect(result.bazaarInvalid).toBe(false);
});
