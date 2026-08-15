import type {
  StreamOverlayCropSettingsPayload,
  StreamOverlayDisplayMode,
  StreamServiceStatus
} from '../../types/backend';
import { defaultCropSettings } from '../../api/previewDefaults';
import {
  streamProblemFromError,
  streamRuntimeProblem,
  type StreamNotice,
  type StreamProblem,
  type StreamProblemCode
} from './streamProblems';

export type StreamCapabilityPhase =
  'loading' | 'available' | 'degraded' | 'unavailable';

type StatusOperation = 'restart' | 'window';
type CropOperation = 'load' | 'crop' | 'display_mode' | 'reset';
type OneOffAction = 'copy' | 'open_overlay' | 'open_settings';

export interface StreamCommandPort {
  ensureSession(): Promise<StreamServiceStatus>;
  getStatus(): Promise<StreamServiceStatus>;
  restartSession(): Promise<StreamServiceStatus>;
  setWindow(offset: number): Promise<StreamServiceStatus>;
  loadCropSettings(): Promise<StreamOverlayCropSettingsPayload>;
  applyCropCode(code: string): Promise<StreamOverlayCropSettingsPayload>;
  saveDisplayMode(
    displayMode: StreamOverlayDisplayMode
  ): Promise<StreamOverlayCropSettingsPayload>;
  resetCropSettings(): Promise<StreamOverlayCropSettingsPayload>;
}

export interface StreamScheduler {
  setInterval(callback: () => void, delayMs: number): unknown;
  clearInterval(handle: unknown): void;
  setTimeout(callback: () => void, delayMs: number): unknown;
  clearTimeout(handle: unknown): void;
}

export interface StreamClipboard {
  writeText(value: string): Promise<void>;
}

export interface StreamOpener {
  open(url: string): Promise<void>;
}

export interface StreamPageSnapshot {
  service: {
    phase: Extract<StreamCapabilityPhase, 'loading' | 'available' | 'degraded'>;
    status: StreamServiceStatus | null;
    problem: StreamProblem | null;
    operation: Extract<StatusOperation, 'restart'> | null;
    canRestart: boolean;
  };
  polling: {
    phase: Extract<StreamCapabilityPhase, 'loading' | 'available' | 'degraded'>;
    freshness: 'unknown' | 'fresh' | 'stale';
    problem: StreamProblem | null;
    operation: 'poll' | 'retry' | null;
  };
  window: {
    phase: StreamCapabilityPhase;
    problem: StreamProblem | null;
    operation: Extract<StatusOperation, 'window'> | null;
    canMoveMoreHistory: boolean;
    canMoveLessHistory: boolean;
  };
  crop: {
    phase: Extract<StreamCapabilityPhase, 'loading' | 'available' | 'degraded'>;
    settings: StreamOverlayCropSettingsPayload;
    code: string;
    problem: StreamProblem | null;
    operation: CropOperation | null;
    canEdit: boolean;
  };
  oneOff: {
    operations: Record<OneOffAction, boolean>;
    problems: Record<OneOffAction, StreamProblem | null>;
    obsUrl: string | null;
    settingsUrl: string | null;
    canOpenOverlay: boolean;
    canCopyObsUrl: boolean;
    canOpenSettings: boolean;
  };
  notice: StreamNotice | null;
}

export interface StreamWorkflowIntents {
  restart(): Promise<boolean>;
  retryStatus(): Promise<boolean>;
  reloadCropSettings(): Promise<boolean>;
  copyObsUrl(): Promise<boolean>;
  openOverlay(): Promise<boolean>;
  openSettings(): Promise<boolean>;
  changeDisplayMode(displayMode: StreamOverlayDisplayMode): Promise<boolean>;
  setCropCode(value: string): void;
  submitCropCode(): Promise<boolean>;
  resetCropCode(): Promise<boolean>;
  moveWindow(delta: number): Promise<boolean>;
}

export interface StreamWorkflow {
  getSnapshot(): StreamPageSnapshot;
  subscribe(listener: () => void): () => void;
  start(): Promise<void>;
  dispose(): void;
  readonly intents: StreamWorkflowIntents;
}

interface StreamWorkflowPorts {
  commands: StreamCommandPort;
  scheduler: StreamScheduler;
  clipboard: StreamClipboard;
  opener: StreamOpener;
  // The calibration page is served from the stream service's own origin, so it
  // cannot read the app's locale. Read it live, since the user can toggle
  // language while the page is mounted.
  currentLocale: () => string;
}

interface MutableState {
  status: StreamServiceStatus | null;
  serviceLoading: boolean;
  serviceProblem: StreamProblem | null;
  pollingFreshness: 'unknown' | 'fresh' | 'stale';
  pollingProblem: StreamProblem | null;
  pollingRequests: number;
  manualPollingRequests: number;
  cropSettings: StreamOverlayCropSettingsPayload;
  cropCode: string;
  cropLoading: boolean;
  cropProblem: StreamProblem | null;
  statusOperation: StatusOperation | null;
  windowProblem: StreamProblem | null;
  cropOperation: CropOperation | null;
  oneOffOperations: Set<OneOffAction>;
  oneOffProblems: Record<OneOffAction, StreamProblem | null>;
  notice: StreamNotice | null;
}

const POLL_INTERVAL_MS = 2_000;
const POLL_FAILURE_THRESHOLD = 3;
const TRANSIENT_MESSAGE_MS = 3_000;

class DefaultStreamWorkflow implements StreamWorkflow {
  private readonly listeners = new Set<() => void>();
  private state: MutableState = initialState();
  private snapshot: StreamPageSnapshot;
  private started = false;
  private disposed = false;
  private lifecycleEpoch = 0;
  private statusEpoch = 0;
  private latestPollRequest = 0;
  private consecutivePollFailures = 0;
  private intervalHandle: unknown = null;
  private noticeTimeoutHandle: unknown = null;

  readonly intents: StreamWorkflowIntents = {
    restart: () => this.restart(),
    retryStatus: () => this.poll(true),
    reloadCropSettings: () => this.reloadCropSettings(),
    copyObsUrl: () => this.copyObsUrl(),
    openOverlay: () => this.openOverlay(),
    openSettings: () => this.openSettings(),
    changeDisplayMode: (displayMode) => this.changeDisplayMode(displayMode),
    setCropCode: (value) => this.setCropCode(value),
    submitCropCode: () => this.submitCropCode(),
    resetCropCode: () => this.resetCropCode(),
    moveWindow: (delta) => this.moveWindow(delta)
  };

  constructor(private readonly ports: StreamWorkflowPorts) {
    this.snapshot = this.deriveSnapshot();
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
    const statusEpoch = ++this.statusEpoch;
    this.state = initialState();
    this.consecutivePollFailures = 0;
    this.clearNoticeTimer();
    this.publish();

    const statusLoad = this.loadInitialStatus(lifecycle, statusEpoch);
    const cropLoad = this.loadInitialCrop(lifecycle);
    await Promise.all([statusLoad, cropLoad]);
    if (!this.isCurrentLifecycle(lifecycle)) return;

    this.intervalHandle = this.ports.scheduler.setInterval(
      () => void this.poll(false),
      POLL_INTERVAL_MS
    );
  }

  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.started = false;
    this.lifecycleEpoch += 1;
    this.statusEpoch += 1;
    this.latestPollRequest += 1;
    this.state.statusOperation = null;
    this.state.cropOperation = null;
    this.state.oneOffOperations.clear();
    if (this.intervalHandle !== null) {
      this.ports.scheduler.clearInterval(this.intervalHandle);
      this.intervalHandle = null;
    }
    this.clearNoticeTimer();
    this.listeners.clear();
  }

  private async loadInitialStatus(lifecycle: number, epoch: number) {
    try {
      const status = await this.ports.commands.ensureSession();
      if (!this.isCurrentLifecycle(lifecycle) || epoch !== this.statusEpoch) {
        return;
      }
      this.applyStatus(status);
    } catch (caught) {
      if (!this.isCurrentLifecycle(lifecycle) || epoch !== this.statusEpoch) {
        return;
      }
      this.state.serviceProblem = streamProblemFromError(
        caught,
        'stream_service_failed',
        { operation: 'ensure' }
      );
      this.state.pollingFreshness = 'unknown';
    } finally {
      if (this.isCurrentLifecycle(lifecycle) && epoch === this.statusEpoch) {
        this.state.serviceLoading = false;
        this.publish();
      }
    }
  }

  private async loadInitialCrop(lifecycle: number) {
    try {
      const settings = await this.ports.commands.loadCropSettings();
      if (!this.isCurrentLifecycle(lifecycle)) return;
      this.applyCropSettings(settings);
      this.state.cropProblem = null;
    } catch (caught) {
      if (!this.isCurrentLifecycle(lifecycle)) return;
      this.state.cropProblem = streamProblemFromError(
        caught,
        'stream_crop_failed',
        { operation: 'load' }
      );
    } finally {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.cropLoading = false;
        this.publish();
      }
    }
  }

  private async poll(manual: boolean): Promise<boolean> {
    if (this.disposed || this.state.statusOperation !== null) return false;
    const lifecycle = this.lifecycleEpoch;
    const epoch = this.statusEpoch;
    const request = ++this.latestPollRequest;
    this.state.pollingRequests += 1;
    if (manual) this.state.manualPollingRequests += 1;
    this.publish();

    try {
      const status = await this.ports.commands.getStatus();
      if (
        !this.isCurrentLifecycle(lifecycle) ||
        epoch !== this.statusEpoch ||
        request !== this.latestPollRequest
      ) {
        return false;
      }
      this.applyStatus(status);
      return true;
    } catch (caught) {
      if (
        !this.isCurrentLifecycle(lifecycle) ||
        epoch !== this.statusEpoch ||
        request !== this.latestPollRequest
      ) {
        return false;
      }
      this.consecutivePollFailures += 1;
      if (this.consecutivePollFailures >= POLL_FAILURE_THRESHOLD) {
        this.state.pollingFreshness = 'stale';
        this.state.pollingProblem = streamProblemFromError(
          caught,
          'stream_poll_failed',
          { operation: 'poll_status' }
        );
      }
      return false;
    } finally {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.pollingRequests = Math.max(
          0,
          this.state.pollingRequests - 1
        );
        if (manual) {
          this.state.manualPollingRequests = Math.max(
            0,
            this.state.manualPollingRequests - 1
          );
        }
        this.publish();
      }
    }
  }

  private async restart(): Promise<boolean> {
    if (
      this.disposed ||
      this.state.serviceLoading ||
      this.state.statusOperation !== null
    ) {
      return false;
    }
    const lifecycle = this.lifecycleEpoch;
    this.state.statusOperation = 'restart';
    this.state.serviceProblem = null;
    this.invalidateStatusRequests();
    this.publish();

    try {
      const status = await this.ports.commands.restartSession();
      if (!this.isCurrentLifecycle(lifecycle)) return false;
      this.applyStatus(status);
      return true;
    } catch (caught) {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.serviceProblem = streamProblemFromError(
          caught,
          'stream_service_failed',
          { operation: 'restart' }
        );
        this.state.pollingFreshness = 'stale';
      }
      return false;
    } finally {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.statusOperation = null;
        this.publish();
      }
    }
  }

  private async reloadCropSettings(): Promise<boolean> {
    if (
      this.disposed ||
      this.state.cropLoading ||
      this.state.cropOperation !== null
    ) {
      return false;
    }
    const lifecycle = this.lifecycleEpoch;
    this.state.cropLoading = true;
    this.state.cropOperation = 'load';
    this.state.cropProblem = null;
    this.publish();
    try {
      const settings = await this.ports.commands.loadCropSettings();
      if (!this.isCurrentLifecycle(lifecycle)) return false;
      this.applyCropSettings(settings);
      return true;
    } catch (caught) {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.cropProblem = streamProblemFromError(
          caught,
          'stream_crop_failed',
          { operation: 'load' }
        );
      }
      return false;
    } finally {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.cropLoading = false;
        this.state.cropOperation = null;
        this.publish();
      }
    }
  }

  private copyObsUrl() {
    const url = this.state.status?.overlay_url;
    if (!url) return Promise.resolve(false);
    return this.runOneOff(
      'copy',
      () => this.ports.clipboard.writeText(url),
      'stream_copy_failed',
      { operation: 'copy_obs_url' },
      { code: 'stream_obs_url_copied', params: {} }
    );
  }

  private openOverlay() {
    const url = this.state.status?.overlay_url;
    if (!url) return Promise.resolve(false);
    return this.runOneOff(
      'open_overlay',
      () => this.ports.opener.open(url),
      'stream_open_failed',
      { operation: 'open_overlay' }
    );
  }

  private openSettings() {
    const url = this.state.status?.settings_url;
    if (!url) return Promise.resolve(false);
    const localized = withLocaleParam(url, this.ports.currentLocale());
    return this.runOneOff(
      'open_settings',
      () => this.ports.opener.open(localized),
      'stream_open_failed',
      { operation: 'open_settings' }
    );
  }

  private changeDisplayMode(displayMode: StreamOverlayDisplayMode) {
    return this.runCropAction(
      'display_mode',
      () => this.ports.commands.saveDisplayMode(displayMode),
      { operation: 'save_display_mode' },
      false
    );
  }

  private setCropCode(value: string) {
    if (this.disposed) return;
    this.state.cropCode = value;
    this.publish();
  }

  private submitCropCode() {
    return this.runCropAction(
      'crop',
      () => this.ports.commands.applyCropCode(this.state.cropCode.trim()),
      { operation: 'apply_code' },
      true,
      { code: 'stream_crop_saved', params: {} }
    );
  }

  private resetCropCode() {
    return this.runCropAction(
      'reset',
      () => this.ports.commands.resetCropSettings(),
      { operation: 'reset' },
      true,
      { code: 'stream_crop_reset', params: {} }
    );
  }

  private async moveWindow(delta: number): Promise<boolean> {
    if (
      this.disposed ||
      this.state.statusOperation !== null ||
      !this.state.status?.running ||
      this.state.pollingFreshness !== 'fresh'
    ) {
      return false;
    }
    const lifecycle = this.lifecycleEpoch;
    const offset = Math.max(
      0,
      Math.trunc(this.state.status.active_window_offset + delta)
    );
    this.state.statusOperation = 'window';
    this.state.windowProblem = null;
    this.invalidateStatusRequests();
    this.publish();

    try {
      const status = await this.ports.commands.setWindow(offset);
      if (!this.isCurrentLifecycle(lifecycle)) return false;
      this.applyStatus(status);
      return true;
    } catch (caught) {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.windowProblem = streamProblemFromError(
          caught,
          'stream_window_failed',
          { operation: 'set_window', offset: String(offset) }
        );
      }
      return false;
    } finally {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.statusOperation = null;
        this.publish();
      }
    }
  }

  private async runCropAction(
    operation: Exclude<CropOperation, 'load'>,
    task: () => Promise<StreamOverlayCropSettingsPayload>,
    params: Record<string, string>,
    updateCode: boolean,
    notice: StreamNotice | null = null
  ): Promise<boolean> {
    if (
      this.disposed ||
      this.state.cropLoading ||
      this.state.cropOperation !== null
    ) {
      return false;
    }
    const lifecycle = this.lifecycleEpoch;
    this.state.cropOperation = operation;
    this.state.cropProblem = null;
    if (notice) this.clearNotice();
    this.publish();

    try {
      const settings = await task();
      if (!this.isCurrentLifecycle(lifecycle)) return false;
      this.applyCropSettings(settings, updateCode);
      if (notice) this.showNotice(notice);
      return true;
    } catch (caught) {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.cropProblem = streamProblemFromError(
          caught,
          'stream_crop_failed',
          params
        );
      }
      return false;
    } finally {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.cropOperation = null;
        this.publish();
      }
    }
  }

  private async runOneOff(
    action: OneOffAction,
    task: () => Promise<void>,
    fallbackCode: StreamProblemCode,
    params: Record<string, string>,
    notice: StreamNotice | null = null
  ): Promise<boolean> {
    if (this.disposed || this.state.oneOffOperations.has(action)) return false;
    const lifecycle = this.lifecycleEpoch;
    this.state.oneOffOperations.add(action);
    this.state.oneOffProblems[action] = null;
    if (notice) this.clearNotice();
    this.publish();

    try {
      await task();
      if (!this.isCurrentLifecycle(lifecycle)) return false;
      if (notice) this.showNotice(notice);
      return true;
    } catch (caught) {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.oneOffProblems[action] = streamProblemFromError(
          caught,
          fallbackCode,
          params
        );
      }
      return false;
    } finally {
      if (this.isCurrentLifecycle(lifecycle)) {
        this.state.oneOffOperations.delete(action);
        this.publish();
      }
    }
  }

  private applyStatus(status: StreamServiceStatus) {
    this.state.status = status;
    this.state.serviceProblem = status.last_error
      ? streamRuntimeProblem(status.last_error)
      : null;
    this.state.pollingFreshness = 'fresh';
    this.state.pollingProblem = null;
    this.consecutivePollFailures = 0;
  }

  private applyCropSettings(
    settings: StreamOverlayCropSettingsPayload,
    updateCode = true
  ) {
    this.state.cropSettings = settings;
    if (updateCode) this.state.cropCode = settings.code;
    this.state.cropProblem = null;
  }

  private invalidateStatusRequests() {
    this.statusEpoch += 1;
    this.latestPollRequest += 1;
  }

  private showNotice(notice: StreamNotice) {
    const lifecycle = this.lifecycleEpoch;
    this.clearNoticeTimer();
    this.state.notice = notice;
    this.noticeTimeoutHandle = this.ports.scheduler.setTimeout(() => {
      this.noticeTimeoutHandle = null;
      if (!this.isCurrentLifecycle(lifecycle)) return;
      this.state.notice = null;
      this.publish();
    }, TRANSIENT_MESSAGE_MS);
    this.publish();
  }

  private clearNotice() {
    this.clearNoticeTimer();
    this.state.notice = null;
  }

  private clearNoticeTimer() {
    if (this.noticeTimeoutHandle === null) return;
    this.ports.scheduler.clearTimeout(this.noticeTimeoutHandle);
    this.noticeTimeoutHandle = null;
  }

  private isCurrentLifecycle(lifecycle: number) {
    return !this.disposed && lifecycle === this.lifecycleEpoch;
  }

  private deriveSnapshot(): StreamPageSnapshot {
    const status = this.state.status;
    const servicePhase = this.state.serviceLoading
      ? 'loading'
      : this.state.serviceProblem
        ? 'degraded'
        : 'available';
    const pollingPhase = this.state.serviceLoading
      ? 'loading'
      : this.state.pollingFreshness !== 'fresh'
        ? 'degraded'
        : 'available';
    const authoritativeRunning =
      status?.running === true &&
      this.state.pollingFreshness === 'fresh' &&
      this.state.serviceProblem === null;
    const statusOperationBusy = this.state.statusOperation !== null;
    const windowPhase: StreamCapabilityPhase = this.state.serviceLoading
      ? 'loading'
      : this.state.windowProblem
        ? 'degraded'
        : authoritativeRunning
          ? 'available'
          : 'unavailable';
    const cropPhase = this.state.cropLoading
      ? 'loading'
      : this.state.cropProblem
        ? 'degraded'
        : 'available';
    const copyBusy = this.state.oneOffOperations.has('copy');
    const overlayBusy = this.state.oneOffOperations.has('open_overlay');
    const settingsBusy = this.state.oneOffOperations.has('open_settings');

    return {
      service: {
        phase: servicePhase,
        status,
        problem: this.state.serviceProblem,
        operation: this.state.statusOperation === 'restart' ? 'restart' : null,
        canRestart: !this.state.serviceLoading && !statusOperationBusy
      },
      polling: {
        phase: pollingPhase,
        freshness: this.state.pollingFreshness,
        problem: this.state.pollingProblem,
        operation:
          this.state.manualPollingRequests > 0
            ? 'retry'
            : this.state.pollingRequests > 0
              ? 'poll'
              : null
      },
      window: {
        phase: windowPhase,
        problem: this.state.windowProblem,
        operation: this.state.statusOperation === 'window' ? 'window' : null,
        canMoveMoreHistory: authoritativeRunning && !statusOperationBusy,
        canMoveLessHistory:
          authoritativeRunning &&
          !statusOperationBusy &&
          (status?.active_window_offset ?? 0) > 0
      },
      crop: {
        phase: cropPhase,
        settings: this.state.cropSettings,
        code: this.state.cropCode,
        problem: this.state.cropProblem,
        operation: this.state.cropOperation,
        canEdit: !this.state.cropLoading && this.state.cropOperation === null
      },
      oneOff: {
        operations: {
          copy: copyBusy,
          open_overlay: overlayBusy,
          open_settings: settingsBusy
        },
        problems: { ...this.state.oneOffProblems },
        obsUrl: status?.overlay_url ?? null,
        settingsUrl: status?.settings_url ?? null,
        canOpenOverlay:
          authoritativeRunning && !statusOperationBusy && !overlayBusy,
        canCopyObsUrl: status?.overlay_url != null && !copyBusy,
        canOpenSettings:
          authoritativeRunning && !statusOperationBusy && !settingsBusy
      },
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
    status: null,
    serviceLoading: true,
    serviceProblem: null,
    pollingFreshness: 'unknown',
    pollingProblem: null,
    pollingRequests: 0,
    manualPollingRequests: 0,
    cropSettings: defaultCropSettings,
    cropCode: defaultCropSettings.code,
    cropLoading: true,
    cropProblem: null,
    statusOperation: null,
    windowProblem: null,
    cropOperation: null,
    oneOffOperations: new Set(),
    oneOffProblems: {
      copy: null,
      open_overlay: null,
      open_settings: null
    },
    notice: null
  };
}

function withLocaleParam(url: string, locale: string): string {
  try {
    const parsed = new URL(url);
    parsed.searchParams.set('lang', locale);
    return parsed.toString();
  } catch {
    // A URL the backend did not produce is still worth opening as-is.
    return url;
  }
}

export function createStreamWorkflow(
  ports: StreamWorkflowPorts
): StreamWorkflow {
  return new DefaultStreamWorkflow(ports);
}
