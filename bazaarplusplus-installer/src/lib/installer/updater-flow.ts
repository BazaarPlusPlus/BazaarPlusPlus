import type { Update } from '@tauri-apps/plugin-updater';
import type { MessageKey } from '../i18n.ts';
import {
  checkForAppUpdate,
  downloadAndInstallUpdate,
  formatUpdaterError,
  type UpdaterSnapshot
} from '../updater.ts';

export type TranslateText = (
  key: MessageKey,
  params?: Record<string, string | number>
) => string;

export interface UpdaterModalCopy {
  title: string;
  body: string;
}

export type UpdaterActionDecision =
  | { type: 'noop' }
  | { type: 'check' }
  | { type: 'open_review' }
  | { type: 'open_modal'; modal: UpdaterModalCopy };

export function createCheckingUpdaterSnapshot(
  snapshot: UpdaterSnapshot,
  hasTauriRuntime: boolean
): UpdaterSnapshot {
  return {
    ...snapshot,
    status: hasTauriRuntime ? 'checking' : 'unsupported',
    errorMessage: null
  };
}

export async function runStartupUpdaterCheck(input: {
  snapshot: UpdaterSnapshot;
  hasTauriRuntime: boolean;
  checkForAppUpdateImpl?: typeof checkForAppUpdate;
}): Promise<{
  snapshot: UpdaterSnapshot;
  update: Update | null;
}> {
  const checkForAppUpdateImpl =
    input.checkForAppUpdateImpl ?? checkForAppUpdate;

  if (!input.hasTauriRuntime) {
    return {
      snapshot: createCheckingUpdaterSnapshot(input.snapshot, false),
      update: null
    };
  }

  return checkForAppUpdateImpl();
}

export async function downloadPendingUpdate(input: {
  snapshot: UpdaterSnapshot;
  update: Update;
  t: TranslateText;
  onProgress: (snapshot: UpdaterSnapshot) => void;
  downloadAndInstallUpdateImpl?: typeof downloadAndInstallUpdate;
}): Promise<{
  snapshot: UpdaterSnapshot;
  modal: UpdaterModalCopy;
}> {
  const downloadAndInstallUpdateImpl =
    input.downloadAndInstallUpdateImpl ?? downloadAndInstallUpdate;

  let nextSnapshot: UpdaterSnapshot = {
    ...input.snapshot,
    status: 'downloading',
    errorMessage: null,
    progress: {
      downloadedBytes: 0,
      totalBytes: null
    }
  };
  input.onProgress(nextSnapshot);

  try {
    await downloadAndInstallUpdateImpl(input.update, (progress) => {
      nextSnapshot = {
        ...nextSnapshot,
        status: 'downloading',
        progress
      };
      input.onProgress(nextSnapshot);
    });

    nextSnapshot = {
      ...nextSnapshot,
      status: 'installed'
    };

    return {
      snapshot: nextSnapshot,
      modal: {
        title: input.t('updaterInstalledTitle'),
        body: input.t('updaterInstalledBody', {
          version: nextSnapshot.availableVersion ?? input.update.version
        })
      }
    };
  } catch (error) {
    const errorMessage = formatUpdaterError(error);
    nextSnapshot = {
      ...nextSnapshot,
      status: 'error',
      errorMessage,
      progress: {
        downloadedBytes: 0,
        totalBytes: null
      }
    };

    return {
      snapshot: nextSnapshot,
      modal: {
        title: input.t('updaterErrorTitle'),
        body: input.t('updaterErrorBody', { message: errorMessage })
      }
    };
  }
}

export function resolveUpdaterActionDecision(input: {
  snapshot: UpdaterSnapshot;
  pendingUpdate: Update | null;
  hasTauriRuntime: boolean;
  t: TranslateText;
}): UpdaterActionDecision {
  if (
    input.snapshot.status === 'checking' ||
    input.snapshot.status === 'downloading'
  ) {
    return { type: 'noop' };
  }

  if (!input.hasTauriRuntime) {
    return {
      type: 'open_modal',
      modal: {
        title: input.t('updaterErrorTitle'),
        body: input.t('updaterErrorBody', {
          message: input.t('updaterUnsupported')
        })
      }
    };
  }

  if (input.snapshot.status === 'available' && input.pendingUpdate) {
    return { type: 'open_review' };
  }

  if (input.snapshot.status === 'installed') {
    return {
      type: 'open_modal',
      modal: {
        title: input.t('updaterInstalledTitle'),
        body: input.t('updaterInstalledBody', {
          version:
            input.snapshot.availableVersion ??
            input.snapshot.currentVersion ??
            'unknown'
        })
      }
    };
  }

  if (input.snapshot.status === 'error') {
    if (input.pendingUpdate) {
      return { type: 'open_review' };
    }

    return {
      type: 'open_modal',
      modal: {
        title: input.t('updaterErrorTitle'),
        body: input.t('updaterErrorBody', {
          message: input.snapshot.errorMessage ?? input.t('updaterUnsupported')
        })
      }
    };
  }

  if (input.snapshot.status === 'up-to-date') {
    return {
      type: 'open_modal',
      modal: {
        title: input.t('updaterCurrentTitle'),
        body: input.t('updaterCurrentBody')
      }
    };
  }

  if (input.snapshot.status === 'idle') {
    return { type: 'check' };
  }

  if (input.snapshot.status === 'unsupported') {
    return {
      type: 'open_modal',
      modal: {
        title: input.t('updaterErrorTitle'),
        body: input.t('updaterErrorBody', {
          message: input.t('updaterUnsupported')
        })
      }
    };
  }

  return {
    type: 'open_modal',
    modal: {
      title: input.t('updaterReadyTitle'),
      body: input.t('updaterReadyBody', {
        version: input.snapshot.availableVersion ?? 'unknown'
      })
    }
  };
}
