import { check, type Update } from '@tauri-apps/plugin-updater';
import { relaunch } from '@tauri-apps/plugin-process';
import { hasTauriRuntime } from '../../api/runtime';
import { toErrorMessage } from '../shared/errors';

/**
 * Structural slice of the plugin-updater `Update` resource the machine needs.
 * `downloadAndInstall` must run on the same handle `check()` returned, so the
 * machine keeps the handle alive across user interactions.
 */
export type UpdateHandle = Pick<
  Update,
  'version' | 'body' | 'downloadAndInstall' | 'close'
>;

export type UpdateCheckResult =
  | { status: 'preview' }
  | { status: 'current' }
  | {
      status: 'available';
      version: string;
      notes: string;
      update: UpdateHandle;
    };

/** Tauri-facing effects, injectable for tests. */
export type UpdaterImpl = {
  check: () => Promise<UpdateHandle | null>;
  relaunch: () => Promise<void>;
  hasRuntime: () => boolean;
  isWindows: () => boolean;
};

export const tauriUpdaterImpl: UpdaterImpl = {
  check: () => check(),
  relaunch: () => relaunch(),
  hasRuntime: hasTauriRuntime,
  isWindows: () =>
    typeof navigator !== 'undefined' && navigator.userAgent.includes('Windows')
};

export async function runCheck(impl: UpdaterImpl): Promise<UpdateCheckResult> {
  if (!impl.hasRuntime()) {
    return { status: 'preview' };
  }

  const update = await impl.check();
  return update
    ? {
        status: 'available',
        version: update.version,
        notes: update.body ?? '',
        update
      }
    : { status: 'current' };
}

export type UpdateProgress = { downloaded: number; total: number | null };

export type UpdaterPhase =
  | 'idle'
  | 'checking'
  | 'current'
  | 'preview'
  | 'available'
  | 'downloading'
  | 'installing'
  | 'ready'
  | 'error';

export type UpdaterSnapshot = {
  phase: UpdaterPhase;
  version: string | null;
  notes: string | null;
  progress: UpdateProgress | null;
  error: string | null;
  /** Check errors render inline in the header; install errors in the modal. */
  errorSource: 'check' | 'install' | null;
};

export const initialUpdaterSnapshot: UpdaterSnapshot = {
  phase: 'idle',
  version: null,
  notes: null,
  progress: null,
  error: null,
  errorSource: null
};

/** Phases rendered inside the update modal (vs. inline header feedback). */
export function isUpdateModalPhase(snapshot: UpdaterSnapshot): boolean {
  switch (snapshot.phase) {
    case 'available':
    case 'downloading':
    case 'installing':
    case 'ready':
      return true;
    case 'error':
      return snapshot.errorSource === 'install';
    default:
      return false;
  }
}

export type UpdaterMachine = {
  getSnapshot: () => UpdaterSnapshot;
  checkNow: (options?: { silent?: boolean }) => Promise<void>;
  install: () => Promise<void>;
  restart: () => Promise<void>;
  dismiss: () => void;
};

export function createUpdaterMachine(
  impl: UpdaterImpl,
  onChange: (snapshot: UpdaterSnapshot) => void
): UpdaterMachine {
  let snapshot = initialUpdaterSnapshot;
  let handle: UpdateHandle | null = null;
  let checkInFlight = false;

  const set = (patch: Partial<UpdaterSnapshot>) => {
    snapshot = { ...snapshot, ...patch };
    onChange(snapshot);
  };

  const busy = () =>
    snapshot.phase === 'checking' ||
    snapshot.phase === 'downloading' ||
    snapshot.phase === 'installing';

  const replaceHandle = (next: UpdateHandle | null) => {
    const previous = handle;
    handle = next;
    if (previous && previous !== next) {
      void previous.close().catch(() => undefined);
    }
  };

  const checkNow = async (options?: { silent?: boolean }) => {
    const silent = options?.silent ?? false;
    if (checkInFlight || busy()) return;
    checkInFlight = true;
    if (!silent) {
      set({ phase: 'checking', error: null, errorSource: null });
    }
    try {
      const result = await runCheck(impl);
      if (result.status === 'available') {
        replaceHandle(result.update);
        set({
          phase: 'available',
          version: result.version,
          notes: result.notes,
          error: null,
          errorSource: null
        });
        return;
      }
      if (!silent) {
        set({ phase: result.status });
      }
    } catch (error) {
      // Startup checks stay silent; manual checks surface inline in the header.
      if (!silent) {
        set({
          phase: 'error',
          error: toErrorMessage(error),
          errorSource: 'check'
        });
      }
    } finally {
      checkInFlight = false;
    }
  };

  const install = async () => {
    if (checkInFlight || busy()) return;
    set({
      phase: 'downloading',
      progress: null,
      error: null,
      errorSource: null
    });
    try {
      let update = handle;
      if (!update) {
        // Retry path: a failed downloadAndInstall may have consumed the old
        // handle, so fetch a fresh one instead of reusing it.
        const result = await runCheck(impl);
        if (result.status !== 'available') {
          set({ phase: result.status, progress: null });
          return;
        }
        replaceHandle(result.update);
        update = result.update;
        set({ version: result.version, notes: result.notes });
      }

      // The handle is consumed by downloadAndInstall either way; drop it so a
      // failure re-checks instead of reusing a dead resource.
      handle = null;
      let downloaded = 0;
      await update.downloadAndInstall((event) => {
        if (event.event === 'Started') {
          downloaded = 0;
          set({
            progress: { downloaded, total: event.data.contentLength ?? null }
          });
        } else if (event.event === 'Progress') {
          downloaded += event.data.chunkLength;
          set({
            progress: { downloaded, total: snapshot.progress?.total ?? null }
          });
        } else {
          // 'Finished': download done; the synthetic installing phase covers
          // the gap until the downloadAndInstall promise resolves.
          set({ phase: 'installing' });
        }
      });

      if (impl.isWindows()) {
        // The NSIS installer owns closing and restarting the app on Windows;
        // relaunch() is a best-effort fallback in case the app is still alive.
        try {
          await impl.relaunch();
        } catch {
          set({ phase: 'ready', progress: null });
        }
        return;
      }
      set({ phase: 'ready', progress: null });
    } catch (error) {
      set({
        phase: 'error',
        error: toErrorMessage(error),
        errorSource: 'install',
        progress: null
      });
    }
  };

  const restart = async () => {
    try {
      await impl.relaunch();
    } catch (error) {
      // Rare; keep the ready phase and surface the failure so the user can
      // restart manually.
      set({ error: toErrorMessage(error) });
    }
  };

  const dismiss = () => {
    if (busy()) return;
    set({ phase: 'idle', error: null, errorSource: null, progress: null });
  };

  return {
    getSnapshot: () => snapshot,
    checkNow,
    install,
    restart,
    dismiss
  };
}

// The startup check must run once per app launch even though React StrictMode
// double-mounts the provider, mirroring the previous module-level singleton.
let startupCheckClaimed = false;

export function claimStartupCheck(): boolean {
  if (startupCheckClaimed) return false;
  startupCheckClaimed = true;
  return true;
}
