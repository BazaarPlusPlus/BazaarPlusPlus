import { describe, expect, it, vi } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import {
  defaultCropSettings,
  idleStreamStatus
} from '../../api/previewDefaults';
import type {
  StreamOverlayCropSettingsPayload,
  StreamServiceStatus
} from '../../types/backend';
import {
  createStreamWorkflow,
  type StreamCommandPort,
  type StreamScheduler
} from './streamWorkflow';
import {
  presentStreamProblem,
  presentStreamSnapshot
} from './streamPresentation';

function runningStatus(
  overrides: Partial<StreamServiceStatus> = {}
): StreamServiceStatus {
  return {
    ...idleStreamStatus,
    running: true,
    port: 17654,
    base_url: 'http://127.0.0.1:17654',
    overlay_url: 'http://127.0.0.1:17654/overlay',
    settings_url: 'http://127.0.0.1:17654/settings',
    db: { found: true, path: '/game/BazaarPlusPlusV5/bazaarplusplus.db' },
    ...overrides
  };
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

class FakeScheduler implements StreamScheduler {
  private nextId = 1;
  readonly intervals = new Map<number, () => void>();
  readonly timeouts = new Map<number, () => void>();

  setInterval(callback: () => void) {
    const id = this.nextId++;
    this.intervals.set(id, callback);
    return id;
  }

  clearInterval(handle: unknown) {
    this.intervals.delete(handle as number);
  }

  setTimeout(callback: () => void) {
    const id = this.nextId++;
    this.timeouts.set(id, callback);
    return id;
  }

  clearTimeout(handle: unknown) {
    this.timeouts.delete(handle as number);
  }

  fireIntervals() {
    for (const callback of [...this.intervals.values()]) callback();
  }
}

function commands(
  overrides: Partial<StreamCommandPort> = {}
): StreamCommandPort {
  return {
    ensureSession: vi.fn().mockResolvedValue(runningStatus()),
    getStatus: vi.fn().mockResolvedValue(runningStatus()),
    restartSession: vi.fn().mockResolvedValue(runningStatus()),
    setWindow: vi.fn().mockResolvedValue(runningStatus()),
    loadCropSettings: vi.fn().mockResolvedValue(defaultCropSettings),
    applyCropCode: vi.fn().mockResolvedValue(defaultCropSettings),
    saveDisplayMode: vi.fn().mockResolvedValue(defaultCropSettings),
    resetCropSettings: vi.fn().mockResolvedValue(defaultCropSettings),
    ...overrides
  };
}

function setup(commandOverrides: Partial<StreamCommandPort> = {}) {
  const scheduler = new FakeScheduler();
  const commandPort = commands(commandOverrides);
  const workflow = createStreamWorkflow({
    commands: commandPort,
    scheduler,
    clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    opener: { open: vi.fn().mockResolvedValue(undefined) },
    currentLocale: () => 'zh'
  });
  return { workflow, scheduler, commands: commandPort };
}

async function flush() {
  await Promise.resolve();
  await Promise.resolve();
}

describe('Stream capability state', () => {
  it('publishes crop availability while service initialization is still loading', async () => {
    const status = deferred<StreamServiceStatus>();
    const { workflow } = setup({ ensureSession: () => status.promise });

    const start = workflow.start();
    await flush();

    expect(workflow.getSnapshot().service.phase).toBe('loading');
    expect(workflow.getSnapshot().crop.phase).toBe('available');
    expect(workflow.getSnapshot().crop.canEdit).toBe(true);

    status.resolve(runningStatus());
    await start;
  });

  it('keeps service and window controls usable when crop configuration degrades', async () => {
    const { workflow } = setup({
      loadCropSettings: vi.fn().mockRejectedValue(new Error('crop unavailable'))
    });

    await workflow.start();
    const snapshot = workflow.getSnapshot();

    expect(snapshot.service.phase).toBe('available');
    expect(snapshot.window.phase).toBe('available');
    expect(snapshot.crop.phase).toBe('degraded');
    expect(snapshot.crop.problem).toMatchObject({
      code: 'stream_crop_failed',
      diagnostic: 'crop unavailable'
    });
    expect(snapshot.service.canRestart).toBe(true);
    expect(snapshot.window.canMoveMoreHistory).toBe(true);
    expect(snapshot.crop.canEdit).toBe(true);
  });

  it('marks failed polling as stale without claiming the last running value is authoritative', async () => {
    const getStatus = vi.fn().mockRejectedValue(new Error('poll unavailable'));
    const { workflow, scheduler } = setup({ getStatus });
    await workflow.start();

    for (let attempt = 0; attempt < 3; attempt += 1) {
      scheduler.fireIntervals();
      await flush();
    }

    const stale = workflow.getSnapshot();
    expect(stale.service.status?.running).toBe(true);
    expect(stale.polling).toMatchObject({
      phase: 'degraded',
      freshness: 'stale',
      problem: { code: 'stream_poll_failed' }
    });
    expect(stale.crop.canEdit).toBe(true);

    const zh = presentStreamSnapshot(stale, (key, params) =>
      formatMessage('zh', key, params)
    );
    expect(zh.status.label).toBe(formatMessage('zh', 'streamStatusStale'));
    expect(zh.status.detail).not.toBe(
      formatMessage('zh', 'streamPortDetail', { port: 17654 })
    );

    getStatus.mockResolvedValueOnce(runningStatus({ active_window_offset: 1 }));
    expect(await workflow.intents.retryStatus()).toBe(true);
    expect(workflow.getSnapshot().polling).toMatchObject({
      phase: 'available',
      freshness: 'fresh',
      problem: null
    });
  });

  it('scopes action failures and operation gates to their capabilities', async () => {
    const pendingCrop = deferred<StreamOverlayCropSettingsPayload>();
    const { workflow } = setup({
      applyCropCode: () => pendingCrop.promise,
      setWindow: vi.fn().mockRejectedValue(new Error('window failed'))
    });
    await workflow.start();

    const cropAction = workflow.intents.submitCropCode();
    expect(workflow.getSnapshot().crop.operation).toBe('crop');
    expect(workflow.getSnapshot().service.canRestart).toBe(true);
    expect(workflow.getSnapshot().window.canMoveMoreHistory).toBe(true);

    pendingCrop.reject(new Error('crop failed'));
    expect(await cropAction).toBe(false);
    expect(workflow.getSnapshot().crop.problem?.code).toBe(
      'stream_crop_failed'
    );

    expect(await workflow.intents.moveWindow(1)).toBe(false);
    expect(workflow.getSnapshot().window.problem).toMatchObject({
      code: 'stream_window_failed',
      diagnostic: 'window failed'
    });
    expect(workflow.getSnapshot().crop.canEdit).toBe(true);
    expect(workflow.getSnapshot().service.problem).toBeNull();
  });

  it('re-presents one live workflow in either locale without starting it again', async () => {
    const ensureSession = vi.fn().mockResolvedValue(runningStatus());
    const { workflow } = setup({ ensureSession });
    await workflow.start();
    const snapshot = workflow.getSnapshot();

    const zh = presentStreamSnapshot(snapshot, (key, params) =>
      formatMessage('zh', key, params)
    );
    const en = presentStreamSnapshot(snapshot, (key, params) =>
      formatMessage('en', key, params)
    );

    expect(zh.status.label).not.toBe(en.status.label);
    expect(ensureSession).toHaveBeenCalledTimes(1);
    expect(workflow.getSnapshot()).toBe(snapshot);
  });
});

describe('Stream problem presentation', () => {
  it.each([
    ['stream_service_failed', { operation: 'restart' }],
    ['stream_poll_failed', { operation: 'poll_status' }],
    ['stream_window_failed', { operation: 'set_window' }],
    ['stream_crop_failed', { operation: 'apply_code' }],
    ['stream_copy_failed', { operation: 'copy_obs_url' }],
    ['stream_open_failed', { operation: 'open_overlay' }],
    ['stream_unexpected', {}]
  ] as const)('localizes %s with recovery copy', (code, params) => {
    const problem = { code, params, diagnostic: 'native detail' };
    const zh = presentStreamProblem(problem, (key, values) =>
      formatMessage('zh', key, values)
    );
    const en = presentStreamProblem(problem, (key, values) =>
      formatMessage('en', key, values)
    );

    expect(zh).not.toContain('native detail');
    expect(en).not.toContain('native detail');
    expect(zh).not.toBe(en);
  });
});
