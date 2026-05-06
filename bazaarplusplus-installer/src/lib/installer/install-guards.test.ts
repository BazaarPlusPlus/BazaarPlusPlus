import { test, expect } from 'vitest';

import {
  getInstallRuntimeRisks,
  shouldShowInstallRiskModal
} from './install-guards.ts';

test('getInstallRuntimeRisks only returns the Steam risk when Steam is running', () => {
  const risks = getInstallRuntimeRisks({
    hasTauriRuntime: true,
    steamLaunchOptionsSupported: true,
    steamRunning: true
  });

  expect(risks).toEqual(['steam_running']);
});

test('getInstallRuntimeRisks does not depend on game state', () => {
  const risks = getInstallRuntimeRisks({
    hasTauriRuntime: true,
    steamLaunchOptionsSupported: true,
    steamRunning: true
  });

  expect(risks).toEqual(['steam_running']);
});

test('getInstallRuntimeRisks suppresses all warnings outside Tauri', () => {
  const risks = getInstallRuntimeRisks({
    hasTauriRuntime: false,
    steamLaunchOptionsSupported: true,
    steamRunning: true
  });

  expect(risks).toEqual([]);
});

test('getInstallRuntimeRisks ignores Steam when launch option updates are unsupported', () => {
  const risks = getInstallRuntimeRisks({
    hasTauriRuntime: true,
    steamLaunchOptionsSupported: false,
    steamRunning: true
  });

  expect(risks).toEqual([]);
});

test('shouldShowInstallRiskModal returns true when any runtime risk is present', () => {
  const shouldShow = shouldShowInstallRiskModal(['steam_running']);

  expect(shouldShow).toBe(true);
});

test('shouldShowInstallRiskModal returns false when there are no runtime risks', () => {
  const shouldShow = shouldShowInstallRiskModal([]);

  expect(shouldShow).toBe(false);
});
