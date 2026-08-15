import type {
  InstallState,
  ResetBepinexResult,
  ResetBppDataResult
} from '../../types/backend';
import {
  createConfirmedOperationController,
  type ConfirmedOperationOutcome
} from '../shared/confirmedOperation';
import type { PageRefreshState } from '../shared/pageState';
import {
  installProblemFromError,
  type InstallProblem
} from './installProblems';

export type InstallOperation =
  | 'refresh'
  | 'choose'
  | 'install'
  | 'resetData'
  | 'resetBepinex'
  | 'uninstall'
  | 'launch';

export type InstallPrimaryActionMode =
  'choose-directory' | 'install' | 'repair' | 'launch';

export type InstallPrimaryAction = {
  mode: InstallPrimaryActionMode;
  operation: Extract<InstallOperation, 'choose' | 'install' | 'launch'>;
  disabled: boolean;
  running: boolean;
};

export type InstallConfirmationTarget =
  | { kind: 'install'; gamePath: string }
  | { kind: 'reset-data'; gamePath: string }
  | { kind: 'reset-bepinex'; gamePath: string }
  | { kind: 'uninstall'; gamePath: string };

export type InstallConfirmation =
  | {
      phase: 'confirming' | 'running';
      target: InstallConfirmationTarget;
      problem: null;
    }
  | {
      phase: 'failed';
      target: InstallConfirmationTarget;
      problem: InstallProblem;
    };

export type InstallNoticeCode =
  | 'install_done'
  | 'uninstall_done'
  | 'reset_data_done'
  | 'reset_data_nothing_to_delete'
  | 'reset_bepinex_done'
  | 'reset_bepinex_nothing_to_delete';

export type InstallNotice = {
  id: number;
  code: InstallNoticeCode;
  params: Record<string, string>;
};

export type InstallActionAvailability = {
  refresh: boolean;
  chooseDirectory: boolean;
  requestInstall: boolean;
  requestResetData: boolean;
  requestResetBepinex: boolean;
  requestUninstall: boolean;
  launch: boolean;
  confirm: boolean;
  dismissConfirmation: boolean;
};

export type InstallPageSnapshot =
  | {
      phase: 'initial-loading';
      data: null;
      refresh: null;
      problem: null;
      operation: InstallOperation | null;
      confirmation: InstallConfirmation | null;
      primaryAction: null;
      actions: InstallActionAvailability;
      actionProblem: InstallProblem | null;
      reconciliationProblem: InstallProblem | null;
      notice: InstallNotice | null;
    }
  | {
      phase: 'blocking-failure';
      data: null;
      refresh: null;
      problem: InstallProblem;
      operation: InstallOperation | null;
      confirmation: InstallConfirmation | null;
      primaryAction: null;
      actions: InstallActionAvailability;
      actionProblem: InstallProblem | null;
      reconciliationProblem: InstallProblem | null;
      notice: InstallNotice | null;
    }
  | {
      phase: 'ready';
      data: InstallState;
      refresh: PageRefreshState<InstallProblem>;
      problem: null;
      operation: InstallOperation | null;
      confirmation: InstallConfirmation | null;
      primaryAction: InstallPrimaryAction;
      actions: InstallActionAvailability;
      actionProblem: InstallProblem | null;
      reconciliationProblem: InstallProblem | null;
      notice: InstallNotice | null;
    };

export interface InstallCommandPort {
  loadInstallState(gamePath?: string | null): Promise<InstallState>;
  chooseGameDirectory(): Promise<{ game_path: string | null }>;
  installMod(gamePath: string): Promise<InstallState>;
  resetBppData(gamePath: string): Promise<ResetBppDataResult>;
  resetBepinex(gamePath: string): Promise<ResetBepinexResult>;
  uninstallMod(gamePath: string): Promise<InstallState>;
  launchGame(): Promise<void>;
}

export interface InstallWorkflowIntents {
  refresh(): Promise<boolean>;
  chooseDirectory(): Promise<boolean>;
  requestInstall(): boolean;
  requestResetData(): boolean;
  requestResetBepinex(): boolean;
  requestUninstall(): boolean;
  confirm(): Promise<boolean>;
  dismissConfirmation(): boolean;
  launch(): Promise<boolean>;
  acknowledgeNotice(id: number): void;
}

export interface InstallWorkflow {
  getSnapshot(): InstallPageSnapshot;
  subscribe(listener: () => void): () => void;
  start(): Promise<void>;
  dispose(): void;
  readonly intents: InstallWorkflowIntents;
}

interface InstallWorkflowPorts {
  commands: InstallCommandPort;
}

type ResourcePhase =
  | { kind: 'initial-loading' }
  | { kind: 'blocking-failure'; problem: InstallProblem }
  | {
      kind: 'ready';
      data: InstallState;
      refresh: PageRefreshState<InstallProblem>;
    };

interface MutableState {
  resource: ResourcePhase;
  operation: InstallOperation | null;
  actionProblem: InstallProblem | null;
  reconciliationProblem: InstallProblem | null;
  notice: InstallNotice | null;
}

class DefaultInstallWorkflow implements InstallWorkflow {
  private readonly listeners = new Set<() => void>();
  private readonly confirmation = createConfirmedOperationController<
    InstallConfirmationTarget,
    InstallProblem
  >();
  private state: MutableState = initialState();
  private snapshot: InstallPageSnapshot;
  private started = false;
  private disposed = false;
  private lifecycleEpoch = 0;
  private requestId = 0;
  private noticeId = 0;

  readonly intents: InstallWorkflowIntents = {
    refresh: () => this.refresh(),
    chooseDirectory: () => this.chooseDirectory(),
    requestInstall: () => this.requestInstall(),
    requestResetData: () => this.requestResetData(),
    requestResetBepinex: () => this.requestResetBepinex(),
    requestUninstall: () => this.requestUninstall(),
    confirm: () => this.confirm(),
    dismissConfirmation: () => this.dismissConfirmation(),
    launch: () => this.launch(),
    acknowledgeNotice: (id) => this.acknowledgeNotice(id)
  };

  constructor(private readonly ports: InstallWorkflowPorts) {
    this.snapshot = this.deriveSnapshot();
    this.confirmation.subscribe(() => {
      this.publish();
    });
  }

  getSnapshot = () => this.snapshot;

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  async start() {
    if (this.started) return;
    this.started = true;
    this.disposed = false;
    const lifecycle = ++this.lifecycleEpoch;
    this.state = initialState();
    this.confirmation.clear();
    this.publish();
    await this.loadState(null, lifecycle, 'refresh');
  }

  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.started = false;
    this.lifecycleEpoch += 1;
    this.requestId += 1;
    this.state.operation = null;
    this.confirmation.clear();
    this.listeners.clear();
  }

  private async refresh(): Promise<boolean> {
    if (!this.canEnterLane() || this.hasActiveConfirmation()) return false;
    const lifecycle = this.lifecycleEpoch;
    const path =
      this.state.resource.kind === 'ready'
        ? (this.state.resource.data.selected_game_path ?? null)
        : null;
    return this.loadState(path, lifecycle, 'refresh');
  }

  private async chooseDirectory(): Promise<boolean> {
    if (!this.canEnterLane() || this.hasActiveConfirmation()) return false;
    if (this.state.resource.kind !== 'ready') return false;

    const lifecycle = this.lifecycleEpoch;
    this.beginOperation('choose');
    try {
      const selection = await this.ports.commands.chooseGameDirectory();
      if (!this.isCurrentLifecycle(lifecycle)) return false;
      if (!selection.game_path) {
        return true;
      }
      const data = await this.ports.commands.loadInstallState(
        selection.game_path
      );
      if (!this.isCurrentLifecycle(lifecycle)) return false;
      this.applyReadyData(data);
      return true;
    } catch (caught) {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.actionProblem = installProblemFromError(caught);
      }
      return false;
    } finally {
      this.endOperation(lifecycle);
    }
  }

  private requestInstall(): boolean {
    if (!this.canOpenConfirmation()) return false;
    const data = this.readyData();
    if (!data?.selected_game_path) return false;
    return this.confirmation.request({
      kind: 'install',
      gamePath: data.selected_game_path
    });
  }

  private requestResetData(): boolean {
    if (!this.canOpenConfirmation()) return false;
    const data = this.readyData();
    if (!data?.selected_game_path) return false;
    return this.confirmation.request({
      kind: 'reset-data',
      gamePath: data.selected_game_path
    });
  }

  private requestResetBepinex(): boolean {
    if (!this.canOpenConfirmation()) return false;
    const data = this.readyData();
    if (!data?.selected_game_path) return false;
    return this.confirmation.request({
      kind: 'reset-bepinex',
      gamePath: data.selected_game_path
    });
  }

  private requestUninstall(): boolean {
    if (!this.canOpenConfirmation()) return false;
    const data = this.readyData();
    if (!data?.selected_game_path) return false;
    return this.confirmation.request({
      kind: 'uninstall',
      gamePath: data.selected_game_path
    });
  }

  private async confirm(): Promise<boolean> {
    if (this.disposed || this.state.operation !== null) return false;
    const current = this.confirmation.getSnapshot();
    if (!current || current.phase === 'running') return false;

    const lifecycle = this.lifecycleEpoch;
    const operation = operationForConfirmation(current.target);
    this.state.operation = operation;
    this.state.actionProblem = null;
    this.state.reconciliationProblem = null;
    this.clearNotice();
    this.publish();

    const completed = await this.confirmation.run(
      (target) => this.executeConfirmed(target, lifecycle),
      installProblemFromError
    );

    if (this.isCurrentLifecycle(lifecycle)) {
      this.state.operation = null;
      this.publish();
    }
    return completed;
  }

  private dismissConfirmation(): boolean {
    if (this.state.operation !== null) return false;
    return this.confirmation.dismiss();
  }

  private async launch(): Promise<boolean> {
    if (!this.canEnterLane() || this.hasActiveConfirmation()) return false;
    if (this.state.resource.kind !== 'ready') return false;

    const lifecycle = this.lifecycleEpoch;
    this.beginOperation('launch');
    try {
      await this.ports.commands.launchGame();
      return this.isCurrentLifecycle(lifecycle);
    } catch (caught) {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.actionProblem = installProblemFromError(caught);
      }
      return false;
    } finally {
      this.endOperation(lifecycle);
    }
  }

  private acknowledgeNotice(id: number) {
    if (this.disposed) return;
    if (this.state.notice?.id !== id) return;
    this.state.notice = null;
    this.publish();
  }

  private async executeConfirmed(
    target: InstallConfirmationTarget,
    lifecycle: number
  ): Promise<ConfirmedOperationOutcome<InstallProblem>> {
    try {
      switch (target.kind) {
        case 'install': {
          const next = await this.ports.commands.installMod(target.gamePath);
          if (!this.isCurrentLifecycle(lifecycle)) return { ok: true };
          this.applyReadyData(next);
          this.showNotice('install_done');
          return { ok: true };
        }
        case 'reset-data': {
          const data = this.readyData();
          if (data && !data.has_resettable_data) {
            if (!this.isCurrentLifecycle(lifecycle)) return { ok: true };
            this.showNotice('reset_data_nothing_to_delete');
            return { ok: true };
          }
          const result = await this.ports.commands.resetBppData(
            target.gamePath
          );
          if (!this.isCurrentLifecycle(lifecycle)) return { ok: true };
          this.applyReadyData(result.state);
          this.showNotice(
            result.removed_data
              ? 'reset_data_done'
              : 'reset_data_nothing_to_delete'
          );
          return { ok: true };
        }
        case 'reset-bepinex': {
          const data = this.readyData();
          if (data && !data.has_bepinex_files) {
            if (!this.isCurrentLifecycle(lifecycle)) return { ok: true };
            this.showNotice('reset_bepinex_nothing_to_delete');
            return { ok: true };
          }
          const result = await this.ports.commands.resetBepinex(
            target.gamePath
          );
          if (!this.isCurrentLifecycle(lifecycle)) return { ok: true };
          this.applyReadyData(result.state);
          this.showNotice(
            result.removed
              ? 'reset_bepinex_done'
              : 'reset_bepinex_nothing_to_delete'
          );
          return { ok: true };
        }
        case 'uninstall': {
          const next = await this.ports.commands.uninstallMod(target.gamePath);
          if (!this.isCurrentLifecycle(lifecycle)) return { ok: true };
          this.applyReadyData(next);
          this.showNotice('uninstall_done');
          return { ok: true };
        }
      }
    } catch (caught) {
      if (!this.isCurrentLifecycle(lifecycle)) {
        return { ok: false, problem: installProblemFromError(caught) };
      }
      const problem = installProblemFromError(caught);
      await this.reconcile(target.gamePath, lifecycle);
      return { ok: false, problem };
    }
  }

  private async loadState(
    gamePath: string | null,
    lifecycle: number,
    operation: Extract<InstallOperation, 'refresh'>
  ): Promise<boolean> {
    if (!this.isCurrentLifecycle(lifecycle)) return false;
    if (this.state.operation !== null) return false;

    const requestId = ++this.requestId;
    this.state.operation = operation;
    this.state.actionProblem = null;
    this.state.reconciliationProblem = null;
    this.clearNotice();

    if (this.state.resource.kind === 'ready') {
      this.state.resource = {
        ...this.state.resource,
        refresh: { phase: 'refreshing' }
      };
    } else {
      this.state.resource = { kind: 'initial-loading' };
    }
    this.publish();

    try {
      const data = await this.ports.commands.loadInstallState(gamePath);
      if (!this.isCurrentLifecycle(lifecycle) || requestId !== this.requestId) {
        return false;
      }
      this.applyReadyData(data);
      return true;
    } catch (caught) {
      if (!this.isCurrentLifecycle(lifecycle) || requestId !== this.requestId) {
        return false;
      }
      const problem = installProblemFromError(caught);
      if (this.state.resource.kind === 'ready') {
        this.state.resource = {
          ...this.state.resource,
          refresh: { phase: 'failed', problem }
        };
      } else {
        this.state.resource = { kind: 'blocking-failure', problem };
      }
      return false;
    } finally {
      if (this.isCurrentLifecycle(lifecycle) && requestId === this.requestId) {
        this.state.operation = null;
        this.publish();
      }
    }
  }

  private async reconcile(gamePath: string, lifecycle: number) {
    try {
      const data = await this.ports.commands.loadInstallState(gamePath);
      if (!this.isCurrentLifecycle(lifecycle)) return;
      this.applyReadyData(data);
      this.state.reconciliationProblem = null;
    } catch (caught) {
      if (!this.isCurrentLifecycle(lifecycle)) return;
      this.state.reconciliationProblem = installProblemFromError(caught);
    }
  }

  private beginOperation(operation: InstallOperation) {
    this.state.operation = operation;
    this.state.actionProblem = null;
    this.state.reconciliationProblem = null;
    this.clearNotice();
    this.publish();
  }

  private endOperation(lifecycle: number) {
    if (!this.isCurrentLifecycle(lifecycle)) return;
    this.state.operation = null;
    this.publish();
  }

  private applyReadyData(data: InstallState) {
    this.state.resource = {
      kind: 'ready',
      data,
      refresh: { phase: 'idle' }
    };
  }

  private showNotice(code: InstallNoticeCode) {
    this.noticeId += 1;
    this.state.notice = { id: this.noticeId, code, params: {} };
  }

  private clearNotice() {
    this.state.notice = null;
  }

  private readyData(): InstallState | null {
    return this.state.resource.kind === 'ready'
      ? this.state.resource.data
      : null;
  }

  private hasActiveConfirmation() {
    return this.confirmation.getSnapshot() !== null;
  }

  private canEnterLane() {
    return !this.disposed && this.state.operation === null;
  }

  private canOpenConfirmation() {
    return (
      this.canEnterLane() &&
      !this.hasActiveConfirmation() &&
      this.state.resource.kind === 'ready' &&
      this.state.resource.refresh.phase !== 'refreshing'
    );
  }

  private isCurrentLifecycle(lifecycle: number) {
    return !this.disposed && lifecycle === this.lifecycleEpoch;
  }

  private deriveSnapshot(): InstallPageSnapshot {
    const confirmation = this.confirmation.getSnapshot();
    const operation = this.state.operation;
    const busy = operation !== null;
    const confirmationActive = confirmation !== null;
    const confirmationRunning = confirmation?.phase === 'running';
    const confirmationFailed = confirmation?.phase === 'failed';
    const confirmationConfirming = confirmation?.phase === 'confirming';
    const data = this.readyData();
    const refreshing =
      this.state.resource.kind === 'ready' &&
      this.state.resource.refresh.phase === 'refreshing';

    const baseActions: InstallActionAvailability = {
      refresh: false,
      chooseDirectory: false,
      requestInstall: false,
      requestResetData: false,
      requestResetBepinex: false,
      requestUninstall: false,
      launch: false,
      confirm: false,
      dismissConfirmation: false
    };

    if (this.state.resource.kind === 'initial-loading') {
      return {
        phase: 'initial-loading',
        data: null,
        refresh: null,
        problem: null,
        operation,
        confirmation,
        primaryAction: null,
        actions: baseActions,
        actionProblem: this.state.actionProblem,
        reconciliationProblem: this.state.reconciliationProblem,
        notice: this.state.notice
      };
    }

    if (this.state.resource.kind === 'blocking-failure') {
      return {
        phase: 'blocking-failure',
        data: null,
        refresh: null,
        problem: this.state.resource.problem,
        operation,
        confirmation,
        primaryAction: null,
        actions: {
          ...baseActions,
          refresh: !busy && !confirmationActive
        },
        actionProblem: this.state.actionProblem,
        reconciliationProblem: this.state.reconciliationProblem,
        notice: this.state.notice
      };
    }

    const ready = this.state.resource;
    const primaryAction = deriveInstallPrimaryAction(
      ready.data,
      operation,
      refreshing || confirmationActive
    );

    return {
      phase: 'ready',
      data: ready.data,
      refresh: ready.refresh,
      problem: null,
      operation,
      confirmation,
      primaryAction,
      actions: {
        refresh: !busy && !confirmationActive && !refreshing,
        chooseDirectory: !busy && !confirmationActive && !refreshing,
        requestInstall:
          !busy &&
          !confirmationActive &&
          !refreshing &&
          !!ready.data.selected_game_path &&
          (ready.data.actions.can_install || ready.data.actions.can_reinstall),
        requestResetData:
          !busy &&
          !confirmationActive &&
          !refreshing &&
          ready.data.actions.can_reset_data,
        requestResetBepinex:
          !busy &&
          !confirmationActive &&
          !refreshing &&
          ready.data.actions.can_reset_bepinex,
        requestUninstall:
          !busy &&
          !confirmationActive &&
          !refreshing &&
          ready.data.actions.can_uninstall,
        launch:
          !busy &&
          !confirmationActive &&
          !refreshing &&
          ready.data.actions.can_launch,
        confirm:
          confirmationActive &&
          !busy &&
          (confirmationConfirming || confirmationFailed),
        dismissConfirmation: confirmationActive && !confirmationRunning && !busy
      },
      actionProblem: this.state.actionProblem,
      reconciliationProblem: this.state.reconciliationProblem,
      notice: this.state.notice
    };
  }

  private publish() {
    if (this.disposed) return;
    this.snapshot = this.deriveSnapshot();
    for (const listener of this.listeners) listener();
  }
}

function initialState(): MutableState {
  return {
    resource: { kind: 'initial-loading' },
    operation: null,
    actionProblem: null,
    reconciliationProblem: null,
    notice: null
  };
}

function operationForConfirmation(
  target: InstallConfirmationTarget
): InstallOperation {
  switch (target.kind) {
    case 'install':
      return 'install';
    case 'reset-data':
      return 'resetData';
    case 'reset-bepinex':
      return 'resetBepinex';
    case 'uninstall':
      return 'uninstall';
  }
}

export function deriveInstallPrimaryAction(
  state: InstallState,
  current: InstallOperation | null,
  blocked: boolean
): InstallPrimaryAction {
  let mode: InstallPrimaryActionMode;
  let operation: InstallPrimaryAction['operation'];
  let allowed: boolean;

  if (!state.selected_game_path || !state.game.path_valid) {
    mode = 'choose-directory';
    operation = 'choose';
    allowed = true;
  } else if (!state.mod_state.installed) {
    mode = 'install';
    operation = 'install';
    allowed = state.actions.can_install;
  } else if (!state.mod_state.ready) {
    mode = 'repair';
    operation = 'install';
    allowed = state.actions.can_reinstall;
  } else {
    mode = 'launch';
    operation = 'launch';
    allowed = state.actions.can_launch;
  }

  return {
    mode,
    operation,
    disabled: blocked || current !== null || !allowed,
    running: current === operation
  };
}

export function createInstallWorkflow(
  ports: InstallWorkflowPorts
): InstallWorkflow {
  return new DefaultInstallWorkflow(ports);
}
