import { test, expect } from 'vitest';

import {
  createPageState,
  selectCustomGamePath,
  selectEffectiveGamePath
} from './state.ts';

test('selectCustomGamePath trims and returns null for empty strings', () => {
  expect(selectCustomGamePath('   ')).toBe(null);
  expect(selectCustomGamePath('  C:\\Games\\The Bazaar  ')).toBe(
    'C:\\Games\\The Bazaar'
  );
});

test('selectEffectiveGamePath prefers custom path over detected path', () => {
  expect(
    selectEffectiveGamePath('D:\\Custom\\Bazaar', 'C:\\Detected\\Bazaar')
  ).toBe('D:\\Custom\\Bazaar');
});

test('selectEffectiveGamePath falls back to detected path', () => {
  expect(selectEffectiveGamePath(null, 'C:\\Detected\\Bazaar')).toBe(
    'C:\\Detected\\Bazaar'
  );
  expect(selectEffectiveGamePath(null, null)).toBe('');
});

test('createPageState computes install prerequisites and version mismatch', () => {
  const state = createPageState({
    actionBusy: 'idle',
    bazaarFound: true,
    selectedGamePath: 'C:\\Games\\The Bazaar',
    detectedGamePath: null,
    isDebugInstallPreview: false,
    bundledBppVersion: '1.2.0',
    installedBppVersion: '1.1.0'
  });

  expect(state.hasPath).toBe(true);
  expect(state.isBusy).toBe(false);
  expect(state.canInstall).toBe(true);
  expect(state.canLaunchGame).toBe(true);
  expect(state.versionMismatch).toBe(true);
});

test('createPageState allows install during debug preview without prerequisites', () => {
  const state = createPageState({
    actionBusy: 'idle',
    bazaarFound: false,
    selectedGamePath: null,
    detectedGamePath: null,
    isDebugInstallPreview: true,
    bundledBppVersion: null,
    installedBppVersion: null
  });

  expect(state.canInstall).toBe(true);
  expect(state.canLaunchGame).toBe(false);
});

test('createPageState allows install when dotnet runtime is missing but game path is valid', () => {
  const state = createPageState({
    actionBusy: 'idle',
    bazaarFound: true,
    selectedGamePath: 'C:\\Games\\The Bazaar',
    detectedGamePath: null,
    isDebugInstallPreview: false,
    bundledBppVersion: '1.2.0',
    installedBppVersion: null
  });

  expect(state.canInstall).toBe(true);
  expect(state.canLaunchGame).toBe(true);
});

test('createPageState does not allow launch when Bazaar is flagged found but no path exists', () => {
  const state = createPageState({
    actionBusy: 'idle',
    bazaarFound: true,
    selectedGamePath: null,
    detectedGamePath: null,
    isDebugInstallPreview: false,
    bundledBppVersion: '1.2.0',
    installedBppVersion: '1.2.0'
  });

  expect(state.hasPath).toBe(false);
  expect(state.canLaunchGame).toBe(false);
});
