import { test, expect } from 'vitest';

import { selectSteamModal } from './steam-modal.ts';
import type { TranslateText } from './types.ts';

const t: TranslateText = (key) => String(key);

test('selectSteamModal uses install-specific copy for install actions', () => {
  const selection = selectSteamModal({
    pendingSteamAction: 'install',
    t
  });

  expect(selection.title).toBe('installRiskTitle');
  expect(selection.body).toBe('installRiskSteamDetected\n\ninstallRiskBody');
  expect(selection.cancelText).toBe('actionContinueInstall');
});

test('selectSteamModal falls back to steam quit copy for uninstall and null', () => {
  const uninstall = selectSteamModal({
    pendingSteamAction: 'uninstall',
    t
  });
  const none = selectSteamModal({
    pendingSteamAction: null,
    t
  });

  expect(uninstall.title).toBe('steamQuitTitle');
  expect(uninstall.body).toBe('steamQuitBody');
  expect(uninstall.cancelText).toBe('actionClose');
  expect(none).toEqual(uninstall);
});
