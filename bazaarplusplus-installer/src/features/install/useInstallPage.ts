import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore
} from 'react';
import { listen } from '@tauri-apps/api/event';
import { hasTauriRuntime } from '../../api/runtime';
import type { InstallState } from '../../types/backend';
import type { MessageKey } from '../../i18n/messages';
import { useI18n, type Translate } from '../../i18n/LocaleProvider';
import { parseResetBppDataError, toErrorMessage } from '../shared/errors';
import { useAsyncAction } from '../shared/useAsyncAction';
import {
  cancelTempoLaunch,
  chooseGameDirectory,
  emptyInstallState,
  installMod,
  launchGame,
  loadInstallState,
  resetBppData,
  uninstallMod
} from './installApi';

type InstallAction =
  | 'load'
  | 'choose'
  | 'install'
  | 'resetData'
  | 'uninstall'
  | 'launch';

const tempoLaunchActivePhases = new Set([
  'prepare',
  'backup',
  'launcher',
  'capture',
  'restore',
  'launch'
]);
const tempoLaunchTerminalPhases = new Set(['done', 'error']);
const tempoLaunchListeners = new Set<() => void>();
let tempoLaunchInFlight = false;

function setTempoLaunchInFlight(next: boolean) {
  if (tempoLaunchInFlight === next) return;
  tempoLaunchInFlight = next;
  tempoLaunchListeners.forEach((listener) => listener());
}

function subscribeTempoLaunch(listener: () => void) {
  tempoLaunchListeners.add(listener);
  return () => {
    tempoLaunchListeners.delete(listener);
  };
}

function getTempoLaunchSnapshot() {
  return tempoLaunchInFlight;
}

export function useInstallPage() {
  const { t } = useI18n();
  const [state, setState] = useState<InstallState>(emptyInstallState);
  const [selectedPath, setSelectedPath] = useState<string | undefined>(
    undefined
  );
  const [message, setMessage] = useState<string | null>(null);
  const flashTimer = useRef<number | null>(null);
  const [resetDataFailurePaths, setResetDataFailurePaths] = useState<string[]>(
    []
  );
  const { action, error, run, busy } = useAsyncAction<InstallAction>();

  // Discrete success confirmations (install/uninstall/reset done) should not
  // linger forever. Tempo launch *progress* messages are set via plain
  // setMessage and intentionally persist until the flow ends.
  const flashMessage = useCallback((next: string) => {
    if (flashTimer.current !== null) {
      window.clearTimeout(flashTimer.current);
    }
    setMessage(next);
    flashTimer.current = window.setTimeout(() => {
      setMessage(null);
      flashTimer.current = null;
    }, 4000);
  }, []);

  useEffect(
    () => () => {
      if (flashTimer.current !== null) {
        window.clearTimeout(flashTimer.current);
      }
    },
    []
  );
  const tempoLaunchBusy = useSyncExternalStore(
    subscribeTempoLaunch,
    getTempoLaunchSnapshot,
    getTempoLaunchSnapshot
  );

  const tempoPhaseMessages: Partial<Record<string, MessageKey>> = useMemo(
    () => ({
      prepare: 'tempoLaunchPrepare',
      backup: 'tempoLaunchBackup',
      launcher: 'tempoLaunchLauncher',
      capture: 'tempoLaunchCapture',
      restore: 'tempoLaunchRestore',
      launch: 'tempoLaunchLaunching',
      done: 'tempoLaunchDone',
      error: 'tempoLaunchFailed'
    }),
    []
  );

  const refresh = useCallback(
    async (gamePath = selectedPath) => {
      await run(
        'load',
        async () => {
          const nextState = await loadInstallState(gamePath);
          setState(nextState);
          setSelectedPath(nextState.selected_game_path ?? gamePath);
        },
        { onStart: () => setMessage(null) }
      );
    },
    [run, selectedPath]
  );

  useEffect(() => {
    void run(
      'load',
      async () => {
        const nextState = await loadInstallState(undefined);
        setState(nextState);
        setSelectedPath(nextState.selected_game_path ?? undefined);
      },
      { onStart: () => setMessage(null) }
    );
  }, [run]);

  // The backend warms up installer context in the background and emits
  // `startup-ready` when done. On slow first launches (Windows) the initial
  // load above can race ahead of warm-up; refresh once the signal arrives so
  // the first screen converges to fully-detected state without user action.
  useEffect(() => {
    if (!hasTauriRuntime()) return;
    const unlisten = listen('startup-ready', () => {
      void refresh();
    });
    return () => {
      void unlisten.then((stop) => stop());
    };
  }, [refresh]);

  useEffect(() => {
    if (!hasTauriRuntime()) return;
    const unlisten = listen<{ phase: string; message: string }>(
      'tempo-launch-status',
      (event) => {
        if (tempoLaunchActivePhases.has(event.payload.phase)) {
          setTempoLaunchInFlight(true);
        } else if (tempoLaunchTerminalPhases.has(event.payload.phase)) {
          setTempoLaunchInFlight(false);
        }
        const key = tempoPhaseMessages[event.payload.phase];
        if (key) setMessage(t(key));
      }
    );
    return () => {
      void unlisten.then((stop) => stop());
    };
  }, [t, tempoPhaseMessages]);

  const chooseDirectory = useCallback(
    () =>
      run('choose', async () => {
        const selection = await chooseGameDirectory();
        if (!selection.game_path) return;
        setSelectedPath(selection.game_path);
        setState(await loadInstallState(selection.game_path));
      }),
    [run]
  );

  const install = useCallback(
    (compatOptIn: boolean) =>
      run(
        'install',
        async () => {
          const path = requireGamePath(state, t);
          setState(await installMod(path, compatOptIn));
          flashMessage(t('installDone'));
        },
        { onStart: () => setMessage(null) }
      ),
    [flashMessage, run, state, t]
  );

  const resetData = useCallback(
    () =>
      run(
        'resetData',
        async () => {
          if (!state.has_resettable_data) {
            setResetDataFailurePaths([]);
            flashMessage(t('resetDataNothingToDelete'));
            return;
          }

          const path = requireGamePath(state, t);
          const result = await resetBppData(path);
          setState(result.state);
          setResetDataFailurePaths([]);
          flashMessage(
            result.removed_data
              ? t('resetDataDone')
              : t('resetDataNothingToDelete')
          );
        },
        {
          onStart: () => {
            setMessage(null);
            setResetDataFailurePaths([]);
          },
          errorMessage: (caught) =>
            formatResetBppDataError(caught, t, setResetDataFailurePaths)
        }
      ),
    [flashMessage, run, state, t]
  );

  const uninstall = useCallback(
    () =>
      run(
        'uninstall',
        async () => {
          const path = requireGamePath(state, t);
          setState(await uninstallMod(path));
          flashMessage(t('uninstallDone'));
        },
        { onStart: () => setMessage(null) }
      ),
    [flashMessage, run, state, t]
  );

  const launch = useCallback(
    () =>
      run(
        'launch',
        async () => {
          setTempoLaunchInFlight(true);
          try {
            await launchGame(state.selected_game_path ?? undefined);
          } finally {
            setTempoLaunchInFlight(false);
          }
        },
        {
          onStart: () => setMessage(null),
          errorMessage: (caught) => formatTempoLaunchError(caught, t)
        }
      ),
    [run, state.selected_game_path, t]
  );

  const cancelLaunch = useCallback(() => {
    void cancelTempoLaunch().catch(() => undefined);
  }, []);

  const status = useMemo(() => createInstallStatus(state, t), [state, t]);

  const effectiveAction: InstallAction | null = tempoLaunchBusy
    ? 'launch'
    : action;
  const effectiveBusy = busy || tempoLaunchBusy;

  return {
    state,
    status,
    action: effectiveAction,
    busy: effectiveBusy,
    error,
    message,
    resetDataFailurePaths,
    refresh,
    chooseDirectory,
    install,
    resetData,
    uninstall,
    launch,
    cancelLaunch
  };
}

function requireGamePath(state: InstallState, t: Translate) {
  if (!state.selected_game_path) {
    throw new Error(t('selectGameDirFirst'));
  }
  return state.selected_game_path;
}

function formatResetBppDataError(
  error: unknown,
  t: Translate,
  setFailurePaths: (paths: string[]) => void
) {
  const resetError = parseResetBppDataError(error);
  if (resetError?.code === 'game_running') {
    setFailurePaths([]);
    return t('resetDataBlockedByGame');
  }
  if (resetError?.code === 'partial_failure') {
    setFailurePaths(resetError.paths);
    return t('resetDataPartialFailure', {
      count: Math.max(1, resetError.paths.length)
    });
  }
  setFailurePaths([]);
  return toErrorMessage(error);
}

function formatTempoLaunchError(error: unknown, t: Translate) {
  const message = toErrorMessage(error);
  if (message.includes('tempo_launch_already_in_progress')) {
    return t('tempoLaunchInProgress');
  }
  if (message.includes('tempo_game_already_running')) {
    return t('tempoGameAlreadyRunning');
  }
  if (message.includes('tempo_launcher_not_found')) {
    return t('tempoLauncherNotFound');
  }
  if (message.includes('tempo_restore_failed')) {
    return t('tempoRestoreFailed');
  }
  if (message.includes('tempo_capture_timeout')) {
    return t('tempoCaptureTimeout');
  }
  if (message.includes('tempo_install_needs_repair')) {
    return t('tempoInstallNeedsRepair');
  }
  if (message.includes('tempo_launch_cancelled')) {
    return t('tempoLaunchCancelled');
  }
  return message;
}

function createInstallStatus(state: InstallState, t: Translate) {
  const installed = state.mod_state.installed;
  return {
    gameLabel: state.game.path_valid ? t('gameFilesOk') : t('gameNotFound'),
    gameTone: state.game.path_valid ? ('ok' as const) : ('warn' as const),
    modLabel: installed
      ? state.mod_state.version_matches
        ? t('modReady')
        : t('modNeedsReinstall')
      : t('modNotInstalled'),
    modTone:
      installed && state.mod_state.version_matches
        ? ('ok' as const)
        : ('warn' as const),
    primaryAction: installed ? t('actionReinstall') : t('actionInstall'),
    modVersion:
      state.mod_state.installed_version ??
      state.mod_state.bundled_version ??
      '-',
    steam: state.steam_path ? 'Steam' : '-'
  };
}
