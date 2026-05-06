import {
  check,
  type DownloadEvent,
  type Update
} from '@tauri-apps/plugin-updater';
import { hasTauriRuntime } from './installer/runtime.ts';

export type UpdaterStatus =
  | 'unsupported'
  | 'idle'
  | 'checking'
  | 'up-to-date'
  | 'available'
  | 'downloading'
  | 'installed'
  | 'error';

export type UpdaterProgress = {
  downloadedBytes: number;
  totalBytes: number | null;
};

export type UpdaterSnapshot = {
  status: UpdaterStatus;
  currentVersion: string | null;
  availableVersion: string | null;
  errorMessage: string | null;
  progress: UpdaterProgress;
};

export function createInitialUpdaterSnapshot(): UpdaterSnapshot {
  return {
    status: hasTauriRuntime() ? 'idle' : 'unsupported',
    currentVersion: null,
    availableVersion: null,
    errorMessage: null,
    progress: {
      downloadedBytes: 0,
      totalBytes: null
    }
  };
}

export function formatUpdaterError(error: unknown): string {
  if (error instanceof Error && error.message.trim()) {
    return error.message.trim();
  }

  if (typeof error === 'string' && error.trim()) {
    return error.trim();
  }

  if (typeof error === 'object' && error !== null) {
    const record = error as Record<string, unknown>;
    const candidates = [
      record.message,
      record.error,
      record.details,
      record.reason,
      record.cause
    ];

    for (const candidate of candidates) {
      if (typeof candidate === 'string' && candidate.trim()) {
        return candidate.trim();
      }
    }

    try {
      const serialized = JSON.stringify(error);
      if (serialized && serialized !== '{}') {
        return serialized;
      }
    } catch {
      // ignore serialization failures and fall through to the default message
    }
  }

  return 'Unknown updater error';
}

export function createProgressLabel(progress: UpdaterProgress): string | null {
  if (progress.downloadedBytes <= 0) {
    return null;
  }

  if (!progress.totalBytes || progress.totalBytes <= 0) {
    return `${formatBytes(progress.downloadedBytes)} downloaded`;
  }

  const percent = Math.min(
    100,
    Math.round((progress.downloadedBytes / progress.totalBytes) * 100)
  );
  return `${percent}% · ${formatBytes(progress.downloadedBytes)} / ${formatBytes(progress.totalBytes)}`;
}

export async function checkForAppUpdate(): Promise<{
  snapshot: UpdaterSnapshot;
  update: Update | null;
}> {
  if (!hasTauriRuntime()) {
    return {
      snapshot: createInitialUpdaterSnapshot(),
      update: null
    };
  }

  try {
    const update = await check();

    if (!update) {
      return {
        snapshot: {
          status: 'up-to-date',
          currentVersion: null,
          availableVersion: null,
          errorMessage: null,
          progress: {
            downloadedBytes: 0,
            totalBytes: null
          }
        },
        update: null
      };
    }

    return {
      snapshot: {
        status: 'available',
        currentVersion: update.currentVersion,
        availableVersion: update.version,
        errorMessage: null,
        progress: {
          downloadedBytes: 0,
          totalBytes: null
        }
      },
      update
    };
  } catch (error) {
    return {
      snapshot: {
        status: 'error',
        currentVersion: null,
        availableVersion: null,
        errorMessage: formatUpdaterError(error),
        progress: {
          downloadedBytes: 0,
          totalBytes: null
        }
      },
      update: null
    };
  }
}

export async function downloadAndInstallUpdate(
  update: Update,
  onProgress: (progress: UpdaterProgress) => void
): Promise<void> {
  let downloadedBytes = 0;
  let totalBytes: number | null = null;

  await update.downloadAndInstall((event: DownloadEvent) => {
    if (event.event === 'Started') {
      totalBytes = event.data.contentLength ?? null;
      downloadedBytes = 0;
    }

    if (event.event === 'Progress') {
      downloadedBytes += event.data.chunkLength;
      onProgress({
        downloadedBytes,
        totalBytes
      });
    }

    if (event.event === 'Finished') {
      onProgress({
        downloadedBytes,
        totalBytes
      });
    }
  });
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unitIndex = 0;

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unitIndex]}`;
}
