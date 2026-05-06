import { test, expect } from 'vitest';

import { selectUpdaterButton } from './updater-button.ts';
import type { TranslateText } from './types.ts';

const t: TranslateText = (key, params) => {
  switch (key) {
    case 'updaterReady':
      return `Ready ${params?.version ?? ''}`.trim();
    case 'updaterDownloading':
      return `Downloading ${params?.progress ?? ''}`.trim();
    case 'updaterInstallReady':
      return `Install ${params?.version ?? ''}`.trim();
    default:
      return String(key);
  }
};

test('selectUpdaterButton handles available updates', () => {
  const selection = selectUpdaterButton({
    snapshot: {
      status: 'available',
      currentVersion: '3.0.0',
      availableVersion: '3.1.0',
      errorMessage: null,
      progress: { downloadedBytes: 0, totalBytes: null }
    },
    hasPendingUpdate: true,
    t
  });

  expect(selection.label).toBe('Ready 3.1.0');
  expect(selection.title).toBe('updaterReadyTitle');
  expect(selection.disabled).toBe(false);
  expect(selection.highlighted).toBe(true);
  expect(selection.progressLabel).toBe(null);
});

test('selectUpdaterButton handles downloading updates', () => {
  const selection = selectUpdaterButton({
    snapshot: {
      status: 'downloading',
      currentVersion: '3.0.0',
      availableVersion: '3.1.0',
      errorMessage: null,
      progress: { downloadedBytes: 1536, totalBytes: 2048 }
    },
    hasPendingUpdate: true,
    t
  });

  expect(selection.label).toBe('Downloading 75% · 1.5 KB / 2.0 KB');
  expect(selection.title).toBe('updaterInstalling');
  expect(selection.disabled).toBe(true);
  expect(selection.highlighted).toBe(false);
  expect(selection.progressLabel).toBe('75% · 1.5 KB / 2.0 KB');
});

test('selectUpdaterButton handles error states with and without pending updates', () => {
  const retry = selectUpdaterButton({
    snapshot: {
      status: 'error',
      currentVersion: '3.0.0',
      availableVersion: null,
      errorMessage: 'boom',
      progress: { downloadedBytes: 0, totalBytes: null }
    },
    hasPendingUpdate: true,
    t
  });
  const error = selectUpdaterButton({
    snapshot: {
      status: 'error',
      currentVersion: '3.0.0',
      availableVersion: null,
      errorMessage: 'boom',
      progress: { downloadedBytes: 0, totalBytes: null }
    },
    hasPendingUpdate: false,
    t
  });

  expect(retry.label).toBe('updaterRetry');
  expect(error.label).toBe('updaterErrorState');
  expect(retry.title).toBe('updaterErrorTitle');
  expect(error.title).toBe('updaterErrorTitle');
});
