import { describe, expect, it } from 'vitest';
import { commandClient } from './commandClient';
import {
  defaultCropSettings,
  emptyHistoryRunList,
  emptyInstallState,
  fallbackBootstrap,
  idleStreamStatus
} from './previewDefaults';

describe('browser-preview command adapter', () => {
  it('returns the shared seed references', async () => {
    expect(await commandClient.getInstallState(null)).toBe(emptyInstallState);
    expect(await commandClient.getStreamStatus()).toBe(idleStreamStatus);
    expect(await commandClient.ensureStreamSession(null)).toBe(
      idleStreamStatus
    );
    expect(await commandClient.restartStreamSession(null)).toBe(
      idleStreamStatus
    );
    expect(await commandClient.setStreamWindow(3)).toBe(idleStreamStatus);
    expect(await commandClient.getOverlaySettings()).toBe(defaultCropSettings);
    expect(await commandClient.resetOverlayCrop()).toBe(defaultCropSettings);
    expect(await commandClient.listHistoryRuns(null)).toBe(emptyHistoryRunList);
    expect(
      await commandClient.previewStorageCleanup('screenshots', 'all')
    ).toEqual({
      scope: 'screenshots',
      preview: {
        screenshots: 0,
        orphan_files: 0,
        estimated_bytes: 0,
        skipped_pending_uploads: 0
      }
    });
    expect(
      await commandClient.previewStorageCleanup('run_data', 'all')
    ).toEqual({
      scope: 'run_data',
      preview: {
        runs: 0,
        battles: 0,
        videos: 0,
        estimated_bytes: 0,
        skipped_pending_uploads: 0
      }
    });
    expect(
      await commandClient.executeStorageCleanup('screenshots', 'all')
    ).toEqual({
      scope: 'screenshots',
      result: {
        deleted_rows: 0,
        deleted_files: 0,
        freed_bytes: 0,
        skipped_pending_uploads: 0
      }
    });
    expect(
      await commandClient.executeStorageCleanup('run_data', 'all')
    ).toEqual({
      scope: 'run_data',
      result: {
        deleted_runs: 0,
        deleted_files: 0,
        freed_bytes: 0,
        skipped_pending_uploads: 0
      }
    });
    expect(await commandClient.getAppBootstrap()).toBe(fallbackBootstrap);
  });

  it('reproduces input-dependent overlay fallbacks', async () => {
    expect(await commandClient.applyOverlayCropCode('x')).toEqual({
      ...defaultCropSettings,
      code: 'x'
    });
    expect(await commandClient.saveOverlayDisplayMode('hero')).toEqual({
      ...defaultCropSettings,
      display_mode: 'hero'
    });
  });

  it('returns inline preview literals', async () => {
    expect(await commandClient.chooseGameDirectory()).toEqual({
      game_path: null
    });
    expect(await commandClient.launchGame()).toEqual({ ok: true });
  });

  it('preserves nullable read-only desktop preview results', async () => {
    expect(await commandClient.getHistoryRunDetail('r')).toBeNull();
    expect(await commandClient.deleteRunVideos('r', null)).toBe(
      emptyHistoryRunList
    );
  });

  it('returns typed locale state and null for Tauri unit-returning no-ops', async () => {
    expect(await commandClient.setAppLocale('en')).toEqual({ locale: 'en' });
    expect(await commandClient.revealRunScreenshot('r')).toBeNull();
    expect(await commandClient.revealBattleVideo('b', null)).toBeNull();
  });

  it('keeps preview bootstrap and install gates wired to real defaults', () => {
    expect(fallbackBootstrap.app_version).toBe(__FRONTEND_VERSION__);
    expect(fallbackBootstrap.bundled_bpp_version).toBeNull();
    expect(fallbackBootstrap.links).toBeTruthy();
    expect(emptyInstallState.selected_game_path).toBeNull();
    expect(emptyInstallState.has_resettable_data).toBe(false);
    expect(emptyInstallState.has_bepinex_files).toBe(false);
    expect(emptyInstallState.warnings.map((warning) => warning.code)).toEqual([
      'game_missing'
    ]);
  });

  it('passes native-only preview commands through to the normalized client', async () => {
    await expect(commandClient.installMod('x')).rejects.toBeInstanceOf(Error);
    await expect(
      commandClient.deleteBattleVideo('b', 'v')
    ).rejects.toBeInstanceOf(Error);
  });
});
