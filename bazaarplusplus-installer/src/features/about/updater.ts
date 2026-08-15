import { check, type Update } from '@tauri-apps/plugin-updater';
import { relaunch } from '@tauri-apps/plugin-process';
import { hasTauriRuntime } from '../../api/runtime';
import {
  updaterProblemFromError,
  type UpdaterProblem
} from './updaterProblems';

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
  | 'ready-to-restart'
  | 'restarting'
  | 'failed';

type EmptyUpdaterSnapshot = {
  phase: 'idle' | 'checking' | 'current' | 'preview';
  version: null;
  notes: null;
  progress: null;
  problem: null;
};

type KnownUpdateFields = {
  version: string;
  notes: string;
  problem: null;
};

type KnownUpdateSnapshot = KnownUpdateFields &
  (
    | {
        phase: 'available' | 'installing' | 'ready-to-restart' | 'restarting';
        progress: null;
      }
    | {
        phase: 'downloading';
        progress: UpdateProgress | null;
      }
  );

type FailedUpdaterSnapshot = {
  phase: 'failed';
  version: string | null;
  notes: string | null;
  progress: null;
  problem: UpdaterProblem;
};

export type UpdaterSnapshot =
  EmptyUpdaterSnapshot | KnownUpdateSnapshot | FailedUpdaterSnapshot;

export const initialUpdaterSnapshot: UpdaterSnapshot = {
  phase: 'idle',
  version: null,
  notes: null,
  progress: null,
  problem: null
};

type KnownUpdate = { version: string; notes: string };

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
  let snapshot: UpdaterSnapshot = initialUpdaterSnapshot;
  let knownUpdate: KnownUpdate | null = null;
  let handle: UpdateHandle | null = null;
  let checkInFlight = false;

  const publish = (next: UpdaterSnapshot) => {
    snapshot = next;
    onChange(snapshot);
  };

  const publishEmpty = (phase: EmptyUpdaterSnapshot['phase']) => {
    publish({
      phase,
      version: null,
      notes: null,
      progress: null,
      problem: null
    });
  };

  const publishKnown = (
    phase: Exclude<KnownUpdateSnapshot['phase'], 'downloading'>
  ) => {
    if (!knownUpdate) return;
    publish({
      phase,
      ...knownUpdate,
      progress: null,
      problem: null
    });
  };

  const publishDownloading = (progress: UpdateProgress | null = null) => {
    if (!knownUpdate) return;
    publish({
      phase: 'downloading',
      ...knownUpdate,
      progress,
      problem: null
    });
  };

  const publishFailure = (
    problem: UpdaterProblem,
    update: KnownUpdate | null = knownUpdate
  ) => {
    publish({
      phase: 'failed',
      version: update?.version ?? null,
      notes: update?.notes ?? null,
      progress: null,
      problem
    });
  };

  const busy = () =>
    snapshot.phase === 'checking' ||
    snapshot.phase === 'downloading' ||
    snapshot.phase === 'installing' ||
    snapshot.phase === 'restarting';

  const replaceHandle = (next: UpdateHandle | null) => {
    const previous = handle;
    handle = next;
    if (previous && previous !== next) {
      void previous.close().catch(() => undefined);
    }
  };

  const canCheck = () =>
    snapshot.phase === 'idle' ||
    snapshot.phase === 'current' ||
    snapshot.phase === 'preview' ||
    (snapshot.phase === 'failed' &&
      snapshot.problem.code === 'updater_check_failed');

  const checkNow = async (options?: { silent?: boolean }) => {
    const silent = options?.silent ?? false;
    if (checkInFlight || busy() || !canCheck()) return;
    checkInFlight = true;
    if (!silent) publishEmpty('checking');
    try {
      const result = await runCheck(impl);
      if (result.status === 'available') {
        knownUpdate = { version: result.version, notes: result.notes };
        replaceHandle(result.update);
        publishKnown('available');
        return;
      }
      replaceHandle(null);
      knownUpdate = null;
      if (!silent) publishEmpty(result.status);
    } catch (error) {
      if (!silent) {
        knownUpdate = null;
        publishFailure(updaterProblemFromError(error, 'check'), null);
      }
    } finally {
      checkInFlight = false;
    }
  };

  const canInstall = () =>
    snapshot.phase === 'available' ||
    (snapshot.phase === 'failed' &&
      (snapshot.problem.code === 'updater_download_failed' ||
        snapshot.problem.code === 'updater_install_failed'));

  const install = async () => {
    if (checkInFlight || busy() || !canInstall() || !knownUpdate) return;
    publishDownloading();
    let operation: 'download' | 'install' = 'download';
    try {
      let update = handle;
      if (!update) {
        const result = await runCheck(impl);
        if (result.status !== 'available') {
          knownUpdate = null;
          publishEmpty(result.status);
          return;
        }
        knownUpdate = { version: result.version, notes: result.notes };
        replaceHandle(result.update);
        update = result.update;
        publishDownloading();
      }

      // The handle is consumed by downloadAndInstall either way. A retry must
      // fetch a new resource instead of reusing the consumed native handle.
      handle = null;
      let downloaded = 0;
      await update.downloadAndInstall((event) => {
        if (event.event === 'Started') {
          downloaded = 0;
          publishDownloading({
            downloaded,
            total: event.data.contentLength ?? null
          });
        } else if (event.event === 'Progress') {
          downloaded += event.data.chunkLength;
          publishDownloading({
            downloaded,
            total: snapshot.progress?.total ?? null
          });
        } else {
          operation = 'install';
          publishKnown('installing');
        }
      });

      if (impl.isWindows()) {
        publishKnown('restarting');
        try {
          await impl.relaunch();
        } catch (error) {
          publishFailure(
            updaterProblemFromError(error, 'restart', knownUpdate?.version)
          );
        }
        return;
      }
      publishKnown('ready-to-restart');
    } catch (error) {
      publishFailure(
        updaterProblemFromError(error, operation, knownUpdate?.version)
      );
    }
  };

  const canRestart = () =>
    snapshot.phase === 'ready-to-restart' ||
    (snapshot.phase === 'failed' &&
      snapshot.problem.code === 'updater_restart_failed');

  const restart = async () => {
    if (busy() || !canRestart() || !knownUpdate) return;
    publishKnown('restarting');
    try {
      await impl.relaunch();
    } catch (error) {
      publishFailure(
        updaterProblemFromError(error, 'restart', knownUpdate.version)
      );
    }
  };

  const dismiss = () => {
    if (busy()) return;
    replaceHandle(null);
    knownUpdate = null;
    publish(initialUpdaterSnapshot);
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
