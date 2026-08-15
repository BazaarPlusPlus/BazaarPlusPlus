import { describe, expect, it, vi } from 'vitest';
import { commandClient } from '../../api/commandClient';
import type { InstallState } from '../../types/backend';
import { createInstallCommandPort } from './installApi';
import {
  createInstallWorkflow,
  type InstallCommandPort
} from './installWorkflow';

function installState(overrides: Partial<InstallState> = {}): InstallState {
  return {
    selected_game_path: '/Applications/The Bazaar',
    steam_path: '/Applications/Steam',
    game: { found: true, path_valid: true, display_version: null },
    mod_state: {
      installed: false,
      installed_version: null,
      bundled_version: '4.5.0',
      ready: false
    },
    actions: {
      can_install: true,
      can_reinstall: false,
      can_reset_data: true,
      can_reset_bepinex: true,
      can_uninstall: false,
      can_launch: true
    },
    has_resettable_data: true,
    has_bepinex_files: true,
    warnings: [],
    ...overrides
  };
}

function installedState(overrides: Partial<InstallState> = {}): InstallState {
  return installState({
    mod_state: {
      installed: true,
      installed_version: '4.5.0',
      bundled_version: '4.5.0',
      ready: true
    },
    actions: {
      can_install: false,
      can_reinstall: true,
      can_reset_data: true,
      can_reset_bepinex: true,
      can_uninstall: true,
      can_launch: true
    },
    ...overrides
  });
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function fakeCommands(
  overrides: Partial<InstallCommandPort> = {}
): InstallCommandPort {
  return {
    loadInstallState: vi.fn().mockResolvedValue(installState()),
    chooseGameDirectory: vi
      .fn()
      .mockResolvedValue({ game_path: '/Applications/The Bazaar' }),
    installMod: vi.fn().mockResolvedValue(installedState()),
    resetBppData: vi.fn().mockResolvedValue({
      state: installedState({ has_resettable_data: false }),
      removed_data: true
    }),
    resetBepinex: vi.fn().mockResolvedValue({
      state: installedState({ has_bepinex_files: false }),
      removed: true
    }),
    uninstallMod: vi.fn().mockResolvedValue(installState()),
    launchGame: vi.fn().mockResolvedValue(undefined),
    ...overrides
  };
}

function setup(commandOverrides: Partial<InstallCommandPort> = {}) {
  const commands = fakeCommands(commandOverrides);
  const workflow = createInstallWorkflow({ commands });
  return { workflow, commands };
}

async function flush() {
  await Promise.resolve();
  await Promise.resolve();
}

describe('install workflow loading and refresh', () => {
  it('loads authoritative state on start and retries from blocking failure', async () => {
    const loadInstallState = vi
      .fn()
      .mockRejectedValueOnce(new Error('detect failed'))
      .mockResolvedValueOnce(installState());
    const { workflow } = setup({ loadInstallState });

    await workflow.start();
    expect(workflow.getSnapshot()).toMatchObject({
      phase: 'blocking-failure',
      problem: { diagnostic: 'detect failed' }
    });
    expect(workflow.getSnapshot().actions.refresh).toBe(true);

    expect(await workflow.intents.refresh()).toBe(true);
    expect(workflow.getSnapshot()).toMatchObject({
      phase: 'ready',
      data: { selected_game_path: '/Applications/The Bazaar' },
      refresh: { phase: 'idle' }
    });
  });

  it('preserves ready install state during refresh and after refresh failure', async () => {
    const ready = installState();
    const refresh = deferred<InstallState>();
    const loadInstallState = vi
      .fn()
      .mockResolvedValueOnce(ready)
      .mockImplementationOnce(() => refresh.promise);
    const { workflow } = setup({ loadInstallState });
    await workflow.start();

    const refreshPromise = workflow.intents.refresh();
    await flush();
    expect(workflow.getSnapshot()).toMatchObject({
      phase: 'ready',
      data: ready,
      refresh: { phase: 'refreshing' },
      operation: 'refresh'
    });

    refresh.reject(new Error('refresh failed'));
    expect(await refreshPromise).toBe(false);
    expect(workflow.getSnapshot()).toMatchObject({
      phase: 'ready',
      data: ready,
      refresh: {
        phase: 'failed',
        problem: { diagnostic: 'refresh failed' }
      },
      operation: null
    });
  });
});

describe('install workflow concurrency and directory selection', () => {
  it('rejects conflicting operations instead of queuing them', async () => {
    const slow = deferred<InstallState>();
    const loadInstallState = vi
      .fn()
      .mockResolvedValueOnce(installState())
      .mockImplementation(() => slow.promise);
    const { workflow, commands } = setup({ loadInstallState });
    await workflow.start();

    const first = workflow.intents.refresh();
    await flush();
    expect(await workflow.intents.refresh()).toBe(false);
    expect(await workflow.intents.chooseDirectory()).toBe(false);
    expect(workflow.intents.requestInstall()).toBe(false);
    expect(workflow.intents.requestUninstall()).toBe(false);
    expect(await workflow.intents.launch()).toBe(false);

    slow.resolve(installState());
    expect(await first).toBe(true);
    expect(commands.loadInstallState).toHaveBeenCalledTimes(2);
  });

  it('treats directory selection plus state load as one operation and keeps state on cancel', async () => {
    const ready = installState();
    const selection = deferred<{ game_path: string | null }>();
    const loadInstallState = vi.fn().mockResolvedValue(ready);
    const { workflow, commands } = setup({
      loadInstallState,
      chooseGameDirectory: vi.fn(() => selection.promise)
    });
    await workflow.start();

    const choosePromise = workflow.intents.chooseDirectory();
    await flush();
    expect(workflow.getSnapshot().operation).toBe('choose');
    expect(await workflow.intents.refresh()).toBe(false);

    selection.resolve({ game_path: null });
    expect(await choosePromise).toBe(true);
    expect(workflow.getSnapshot()).toMatchObject({
      phase: 'ready',
      data: ready,
      operation: null
    });
    expect(commands.loadInstallState).toHaveBeenCalledTimes(1);

    const next = installState({
      selected_game_path: '/Games/The Bazaar',
      mod_state: {
        installed: true,
        installed_version: '4.5.0',
        bundled_version: '4.5.0',
        ready: true
      }
    });
    (
      commands.chooseGameDirectory as ReturnType<typeof vi.fn>
    ).mockResolvedValue({ game_path: '/Games/The Bazaar' });
    (commands.loadInstallState as ReturnType<typeof vi.fn>).mockResolvedValue(
      next
    );
    expect(await workflow.intents.chooseDirectory()).toBe(true);
    expect(workflow.getSnapshot().data?.selected_game_path).toBe(
      '/Games/The Bazaar'
    );
  });
});

describe('install workflow confirmation lifecycle', () => {
  it('captures and locks the install target while the operation runs', async () => {
    const installMod = vi.fn().mockResolvedValue(installedState());
    const { workflow } = setup({ installMod });
    await workflow.start();

    expect(workflow.intents.requestInstall()).toBe(true);
    expect(workflow.getSnapshot().confirmation).toMatchObject({
      phase: 'confirming',
      target: {
        kind: 'install',
        gamePath: '/Applications/The Bazaar'
      }
    });
    expect(workflow.getSnapshot().actions.refresh).toBe(false);
    expect(workflow.getSnapshot().actions.chooseDirectory).toBe(false);

    const running = deferred<InstallState>();
    installMod.mockImplementationOnce(() => running.promise);
    const confirmPromise = workflow.intents.confirm();
    await flush();

    expect(workflow.getSnapshot().confirmation).toMatchObject({
      phase: 'running',
      target: {
        gamePath: '/Applications/The Bazaar'
      }
    });
    expect(workflow.intents.dismissConfirmation()).toBe(false);
    expect(await workflow.intents.refresh()).toBe(false);

    running.resolve(installedState());
    expect(await confirmPromise).toBe(true);
    expect(installMod).toHaveBeenCalledWith('/Applications/The Bazaar');
    expect(workflow.getSnapshot().confirmation).toBeNull();
    expect(workflow.getSnapshot().notice?.code).toBe('install_done');
  });

  it('retains fixed reset targets through failure, exact retry, and safe exit', async () => {
    const resetBppData = vi
      .fn()
      .mockRejectedValueOnce(new Error('reset failed'))
      .mockResolvedValueOnce({
        state: installedState({ has_resettable_data: false }),
        removed_data: true
      });
    const loadInstallState = vi
      .fn()
      .mockResolvedValueOnce(installedState())
      .mockResolvedValue(installedState({ has_resettable_data: true }));
    const { workflow } = setup({ loadInstallState, resetBppData });
    await workflow.start();

    expect(workflow.intents.requestResetData()).toBe(true);
    expect(workflow.getSnapshot().confirmation).toMatchObject({
      phase: 'confirming',
      target: {
        kind: 'reset-data',
        gamePath: '/Applications/The Bazaar'
      }
    });

    expect(await workflow.intents.confirm()).toBe(false);
    expect(workflow.getSnapshot().confirmation).toMatchObject({
      phase: 'failed',
      target: {
        kind: 'reset-data',
        gamePath: '/Applications/The Bazaar'
      },
      problem: { diagnostic: 'reset failed' }
    });
    expect(resetBppData).toHaveBeenCalledWith('/Applications/The Bazaar');

    expect(await workflow.intents.confirm()).toBe(true);
    expect(resetBppData).toHaveBeenLastCalledWith('/Applications/The Bazaar');
    expect(workflow.getSnapshot().notice?.code).toBe('reset_data_done');
    expect(workflow.getSnapshot().confirmation).toBeNull();

    expect(workflow.intents.requestResetBepinex()).toBe(true);
    expect(workflow.intents.dismissConfirmation()).toBe(true);
    expect(workflow.getSnapshot().confirmation).toBeNull();
  });

  it('keeps install confirmation open after failure and retries original params', async () => {
    const installMod = vi
      .fn()
      .mockRejectedValueOnce(new Error('install failed'))
      .mockResolvedValueOnce(installedState());
    const { workflow } = setup({ installMod });
    await workflow.start();

    expect(workflow.intents.requestInstall()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(false);
    expect(workflow.getSnapshot().confirmation).toMatchObject({
      phase: 'failed',
      target: {
        kind: 'install',
        gamePath: '/Applications/The Bazaar'
      },
      problem: { diagnostic: 'install failed' }
    });

    expect(await workflow.intents.confirm()).toBe(true);
    expect(installMod).toHaveBeenLastCalledWith('/Applications/The Bazaar');
    expect(workflow.getSnapshot().confirmation).toBeNull();
  });
});

describe('install workflow mutation outcomes', () => {
  it('adopts returned install state without an extra read', async () => {
    const returned = installedState({
      selected_game_path: '/Applications/The Bazaar',
      mod_state: {
        installed: true,
        installed_version: '4.5.0',
        bundled_version: '4.5.0',
        ready: true
      }
    });
    const loadInstallState = vi.fn().mockResolvedValue(installState());
    const { workflow, commands } = setup({
      loadInstallState,
      installMod: vi.fn().mockResolvedValue(returned)
    });
    await workflow.start();
    expect(loadInstallState).toHaveBeenCalledTimes(1);

    expect(workflow.intents.requestInstall()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(true);
    expect(workflow.getSnapshot().data).toBe(returned);
    expect(loadInstallState).toHaveBeenCalledTimes(1);
    expect(commands.installMod).toHaveBeenCalledTimes(1);
  });

  it('reconciles against the fixed target after failure and keeps the action problem primary', async () => {
    const reconciled = installedState({
      warnings: [{ code: 'trampoline_not_ready', params: {} }]
    });
    const loadInstallState = vi
      .fn()
      .mockResolvedValueOnce(installState())
      .mockResolvedValueOnce(reconciled);
    const { workflow } = setup({
      loadInstallState,
      installMod: vi.fn().mockRejectedValue(new Error('partial native work'))
    });
    await workflow.start();

    expect(workflow.intents.requestInstall()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(false);
    expect(workflow.getSnapshot().confirmation).toMatchObject({
      phase: 'failed',
      problem: { diagnostic: 'partial native work' }
    });
    expect(workflow.getSnapshot().data).toEqual(reconciled);
    expect(workflow.getSnapshot().reconciliationProblem).toBeNull();
    expect(loadInstallState).toHaveBeenLastCalledWith(
      '/Applications/The Bazaar'
    );
  });

  it('retains prior snapshot and records a separate reconciliation problem when both calls fail', async () => {
    const ready = installState();
    const loadInstallState = vi
      .fn()
      .mockResolvedValueOnce(ready)
      .mockRejectedValueOnce(new Error('reconcile failed'));
    const { workflow } = setup({
      loadInstallState,
      uninstallMod: vi.fn().mockRejectedValue(new Error('uninstall failed'))
    });
    await workflow.start();

    expect(workflow.intents.requestUninstall()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(false);
    expect(workflow.getSnapshot()).toMatchObject({
      phase: 'ready',
      data: ready,
      confirmation: {
        phase: 'failed',
        problem: { diagnostic: 'uninstall failed' }
      },
      reconciliationProblem: { diagnostic: 'reconcile failed' }
    });
  });

  it('distinguishes removed and nothing-to-delete reset notices', async () => {
    const { workflow, commands } = setup({
      loadInstallState: vi.fn().mockResolvedValue(
        installedState({
          has_resettable_data: false,
          has_bepinex_files: false
        })
      ),
      resetBppData: vi.fn().mockResolvedValue({
        state: installedState({ has_resettable_data: false }),
        removed_data: false
      }),
      resetBepinex: vi.fn().mockResolvedValue({
        state: installedState({ has_bepinex_files: false }),
        removed: false
      })
    });
    await workflow.start();

    expect(workflow.intents.requestResetData()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(true);
    expect(workflow.getSnapshot().notice?.code).toBe(
      'reset_data_nothing_to_delete'
    );
    expect(commands.resetBppData).not.toHaveBeenCalled();

    expect(workflow.intents.requestResetBepinex()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(true);
    expect(workflow.getSnapshot().notice?.code).toBe(
      'reset_bepinex_nothing_to_delete'
    );
    expect(commands.resetBepinex).not.toHaveBeenCalled();
  });

  it('uninstalls through confirmation and launches without install-state reconciliation', async () => {
    const loadInstallState = vi.fn().mockResolvedValue(installedState());
    const { workflow, commands } = setup({
      loadInstallState,
      uninstallMod: vi.fn().mockResolvedValue(installState()),
      launchGame: vi.fn().mockResolvedValue(undefined)
    });
    await workflow.start();

    expect(workflow.intents.requestUninstall()).toBe(true);
    expect(workflow.getSnapshot().confirmation).toMatchObject({
      phase: 'confirming',
      target: { kind: 'uninstall', gamePath: '/Applications/The Bazaar' }
    });
    expect(commands.uninstallMod).not.toHaveBeenCalled();

    expect(await workflow.intents.confirm()).toBe(true);
    expect(workflow.getSnapshot().notice?.code).toBe('uninstall_done');
    expect(workflow.getSnapshot().confirmation).toBeNull();
    expect(commands.uninstallMod).toHaveBeenCalledTimes(1);

    expect(await workflow.intents.launch()).toBe(true);
    expect(commands.launchGame).toHaveBeenCalledTimes(1);
    expect(loadInstallState).toHaveBeenCalledTimes(1);
  });
});

describe('install workflow notices, lifecycle, and availability', () => {
  it('publishes unique notices for acknowledgement without timers', async () => {
    const { workflow } = setup({
      uninstallMod: vi.fn().mockResolvedValue(installState())
    });
    await workflow.start();

    expect(workflow.intents.requestUninstall()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(true);
    const first = workflow.getSnapshot().notice;
    expect(first).toMatchObject({ code: 'uninstall_done' });
    expect(first?.id).toEqual(expect.any(Number));

    workflow.intents.acknowledgeNotice(first!.id);
    expect(workflow.getSnapshot().notice).toBeNull();

    expect(workflow.intents.requestUninstall()).toBe(true);
    expect(await workflow.intents.confirm()).toBe(true);
    const second = workflow.getSnapshot().notice;
    expect(second?.id).not.toBe(first?.id);
  });

  it('ignores late completions after dispose and reloads on a new start', async () => {
    const firstLoad = deferred<InstallState>();
    const secondLoad = deferred<InstallState>();
    const loadInstallState = vi
      .fn()
      .mockImplementationOnce(() => firstLoad.promise)
      .mockImplementationOnce(() => secondLoad.promise);
    const { workflow } = setup({ loadInstallState });

    const firstStart = workflow.start();
    const before = workflow.getSnapshot();
    workflow.dispose();
    firstLoad.resolve(installState({ selected_game_path: '/stale' }));
    await firstStart;
    expect(workflow.getSnapshot()).toBe(before);

    const secondStart = workflow.start();
    secondLoad.resolve(installState({ selected_game_path: '/fresh' }));
    await secondStart;
    expect(workflow.getSnapshot()).toMatchObject({
      phase: 'ready',
      data: { selected_game_path: '/fresh' }
    });
  });

  it.each([
    {
      name: 'invalid or missing path',
      state: installState({
        selected_game_path: null,
        game: { found: false, path_valid: false, display_version: null },
        actions: {
          can_install: false,
          can_reinstall: false,
          can_reset_data: false,
          can_reset_bepinex: false,
          can_uninstall: false,
          can_launch: false
        }
      }),
      mode: 'choose-directory',
      operation: 'choose'
    },
    {
      name: 'valid path without the mod',
      state: installState(),
      mode: 'install',
      operation: 'install'
    },
    {
      name: 'installed version mismatch',
      state: installedState({
        mod_state: {
          installed: true,
          installed_version: '4.4.0',
          bundled_version: '4.5.0',
          ready: false
        }
      }),
      mode: 'repair',
      operation: 'install'
    },
    {
      name: 'platform bootstrap needs repair',
      state: installedState({
        mod_state: {
          installed: true,
          installed_version: '4.5.0',
          bundled_version: '4.5.0',
          ready: false
        }
      }),
      mode: 'repair',
      operation: 'install'
    },
    {
      name: 'current installed state',
      state: installedState(),
      mode: 'launch',
      operation: 'launch'
    }
  ])(
    'derives primary action and availability for $name',
    async ({ state, mode, operation }) => {
      const { workflow } = setup({
        loadInstallState: vi.fn().mockResolvedValue(state)
      });
      await workflow.start();
      const snapshot = workflow.getSnapshot();
      expect(snapshot.phase).toBe('ready');
      if (snapshot.phase !== 'ready') return;
      expect(snapshot.primaryAction).toMatchObject({
        mode,
        operation,
        disabled: false,
        running: false
      });
      expect(snapshot.actions.refresh).toBe(true);
    }
  );

  it('runs through generated/native-shaped and Preview command adapters', async () => {
    const nativeLike = {
      getInstallState: vi.fn().mockResolvedValue(installState()),
      chooseGameDirectory: vi
        .fn()
        .mockResolvedValue({ game_path: '/Applications/The Bazaar' }),
      installMod: vi.fn().mockResolvedValue(installedState()),
      resetBppData: vi.fn().mockResolvedValue({
        state: installedState({ has_resettable_data: false }),
        removed_data: true
      }),
      resetBepinex: vi.fn().mockResolvedValue({
        state: installedState({ has_bepinex_files: false }),
        removed: true
      }),
      uninstallMod: vi.fn().mockResolvedValue(installState()),
      launchGame: vi.fn().mockResolvedValue(undefined)
    } satisfies Parameters<typeof createInstallCommandPort>[0];

    for (const commands of [
      createInstallCommandPort(nativeLike),
      createInstallCommandPort(commandClient)
    ]) {
      const workflow = createInstallWorkflow({ commands });
      await workflow.start();
      expect(workflow.getSnapshot().phase).not.toBe('initial-loading');
      workflow.dispose();
    }
  });
});
