import { test, expect } from 'vitest';

import {
  createCheckingUpdaterSnapshot,
  downloadPendingUpdate,
  resolveUpdaterActionDecision
} from './updater-flow.ts';

function t(key: string, params?: Record<string, string | number>): string {
  if (params?.message) {
    return `${key}:${params.message}`;
  }

  if (params?.version) {
    return `${key}:${params.version}`;
  }

  return key;
}

test('createCheckingUpdaterSnapshot clears updater errors', () => {
  const snapshot = createCheckingUpdaterSnapshot(
    {
      status: 'error',
      currentVersion: '3.0.0',
      availableVersion: null,
      errorMessage: 'boom',
      progress: {
        downloadedBytes: 10,
        totalBytes: 100
      }
    },
    true
  );

  expect(snapshot.status).toBe('checking');
  expect(snapshot.errorMessage).toBe(null);
});

test('downloadPendingUpdate returns installed snapshot and modal copy', async () => {
  const seenStatuses: string[] = [];

  const result = await downloadPendingUpdate({
    snapshot: {
      status: 'available',
      currentVersion: '3.0.0',
      availableVersion: '3.1.0',
      errorMessage: null,
      progress: {
        downloadedBytes: 0,
        totalBytes: null
      }
    },
    update: {
      currentVersion: '3.0.0',
      version: '3.1.0'
    } as never,
    t,
    onProgress: (snapshot) => {
      seenStatuses.push(snapshot.status);
    },
    downloadAndInstallUpdateImpl: async (_update, onProgress) => {
      onProgress({
        downloadedBytes: 50,
        totalBytes: 100
      });
    }
  });

  expect(seenStatuses).toEqual(['downloading', 'downloading']);
  expect(result.snapshot.status).toBe('installed');
  expect(result.modal.title).toBe('updaterInstalledTitle');
});

test('resolveUpdaterActionDecision opens review for available updates', () => {
  const decision = resolveUpdaterActionDecision({
    snapshot: {
      status: 'available',
      currentVersion: '3.0.0',
      availableVersion: '3.1.0',
      errorMessage: null,
      progress: {
        downloadedBytes: 0,
        totalBytes: null
      }
    },
    pendingUpdate: {} as never,
    hasTauriRuntime: true,
    t
  });

  expect(decision).toEqual({ type: 'open_review' });
});
