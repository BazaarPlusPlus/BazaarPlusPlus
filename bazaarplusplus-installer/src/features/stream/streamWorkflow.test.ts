import { describe, expect, it, vi } from 'vitest';
import {
  defaultCropSettings,
  idleStreamStatus
} from '../../api/previewDefaults';
import { commandClient } from '../../api/commandClient';
import type {
  StreamOverlayCropSettingsPayload,
  StreamServiceStatus
} from '../../types/backend';
import { createStreamCommandPort } from './streamApi';
import {
  createStreamWorkflow,
  type StreamCommandPort,
  type StreamScheduler
} from './streamWorkflow';

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

  fireTimeouts() {
    const callbacks = [...this.timeouts.values()];
    this.timeouts.clear();
    for (const callback of callbacks) callback();
  }
}

function fakeCommands(
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

function setup(
  commandOverrides: Partial<StreamCommandPort> = {},
  clipboard = { writeText: vi.fn().mockResolvedValue(undefined) },
  opener = { open: vi.fn().mockResolvedValue(undefined) }
) {
  const scheduler = new FakeScheduler();
  const commands = fakeCommands(commandOverrides);
  const workflow = createStreamWorkflow({
    commands,
    scheduler,
    clipboard,
    opener,
    currentLocale: () => 'zh'
  });
  return { workflow, commands, scheduler, clipboard, opener };
}

async function flush() {
  await Promise.resolve();
  await Promise.resolve();
}

describe('stream workflow lifecycle and effects', () => {
  it('ignores an older poll after a newer poll succeeds', async () => {
    const first = deferred<StreamServiceStatus>();
    const second = deferred<StreamServiceStatus>();
    const getStatus = vi
      .fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);
    const { workflow, scheduler } = setup({ getStatus });
    await workflow.start();

    scheduler.fireIntervals();
    scheduler.fireIntervals();
    second.resolve(runningStatus({ active_window_offset: 2 }));
    await flush();
    first.resolve(runningStatus({ active_window_offset: 1 }));
    await flush();

    expect(workflow.getSnapshot().service.status?.active_window_offset).toBe(2);
  });

  it('does not mark a newer status stale when an older poll fails', async () => {
    const first = deferred<StreamServiceStatus>();
    const second = deferred<StreamServiceStatus>();
    const getStatus = vi
      .fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);
    const { workflow, scheduler } = setup({ getStatus });
    await workflow.start();

    scheduler.fireIntervals();
    scheduler.fireIntervals();
    second.resolve(runningStatus({ active_window_offset: 2 }));
    await flush();
    first.reject(new Error('outdated failure'));
    await flush();

    expect(workflow.getSnapshot().polling).toMatchObject({
      phase: 'available',
      freshness: 'fresh',
      problem: null
    });
  });

  it('does not let a slow poll overwrite a completed restart', async () => {
    const slowPoll = deferred<StreamServiceStatus>();
    const refreshed = runningStatus({ active_window_offset: 3 });
    const { workflow, scheduler } = setup({
      getStatus: vi.fn(() => slowPoll.promise),
      restartSession: vi.fn().mockResolvedValue(refreshed)
    });
    await workflow.start();

    scheduler.fireIntervals();
    await flush();
    expect(await workflow.intents.restart()).toBe(true);
    slowPoll.resolve(runningStatus({ active_window_offset: 1 }));
    await flush();

    expect(workflow.getSnapshot().service.status).toBe(refreshed);
  });

  it('maps an authoritative runtime error separately from polling staleness', async () => {
    const failedStatus = {
      ...idleStreamStatus,
      last_error: 'port occupied'
    };
    const { workflow } = setup({
      ensureSession: vi.fn().mockResolvedValue(failedStatus)
    });
    await workflow.start();

    expect(workflow.getSnapshot().service).toMatchObject({
      phase: 'degraded',
      status: failedStatus,
      problem: {
        code: 'stream_service_failed',
        diagnostic: 'port occupied'
      }
    });
    expect(workflow.getSnapshot().polling.freshness).toBe('fresh');
  });

  it('keeps semantic notices transient and one-off failures target-scoped', async () => {
    const clipboard = {
      writeText: vi
        .fn()
        .mockResolvedValueOnce(undefined)
        .mockRejectedValueOnce(new Error('clipboard denied'))
    };
    const opener = {
      open: vi.fn().mockRejectedValueOnce(new Error('open denied'))
    };
    const { workflow, scheduler } = setup({}, clipboard, opener);
    await workflow.start();

    expect(await workflow.intents.copyObsUrl()).toBe(true);
    expect(workflow.getSnapshot().notice?.code).toBe('stream_obs_url_copied');
    scheduler.fireTimeouts();
    expect(workflow.getSnapshot().notice).toBeNull();

    expect(await workflow.intents.copyObsUrl()).toBe(false);
    expect(workflow.getSnapshot().oneOff.problems.copy).toMatchObject({
      code: 'stream_copy_failed',
      diagnostic: 'clipboard denied'
    });
    expect(await workflow.intents.openOverlay()).toBe(false);
    expect(workflow.getSnapshot().oneOff.problems.open_overlay).toMatchObject({
      code: 'stream_open_failed',
      diagnostic: 'open denied'
    });
    expect(workflow.getSnapshot().crop.canEdit).toBe(true);
  });

  it('ignores responses and cancels timers after disposal', async () => {
    const slow = deferred<StreamServiceStatus>();
    const { workflow, scheduler } = setup({ getStatus: () => slow.promise });
    await workflow.start();

    scheduler.fireIntervals();
    const before = workflow.getSnapshot();
    workflow.dispose();
    slow.resolve(runningStatus({ active_window_offset: 9 }));
    await flush();

    expect(workflow.getSnapshot()).toBe(before);
    expect(scheduler.intervals.size).toBe(0);
  });

  it('supports a dispose-start replay without accepting old initialization', async () => {
    const firstStatus = deferred<StreamServiceStatus>();
    const firstCrop = deferred<StreamOverlayCropSettingsPayload>();
    const secondStatus = deferred<StreamServiceStatus>();
    const secondCrop = deferred<StreamOverlayCropSettingsPayload>();
    const ensureSession = vi
      .fn()
      .mockImplementationOnce(() => firstStatus.promise)
      .mockImplementationOnce(() => secondStatus.promise);
    const loadCropSettings = vi
      .fn()
      .mockImplementationOnce(() => firstCrop.promise)
      .mockImplementationOnce(() => secondCrop.promise);
    const { workflow, scheduler } = setup({
      ensureSession,
      loadCropSettings
    });

    const firstStart = workflow.start();
    workflow.dispose();
    const secondStart = workflow.start();
    firstStatus.resolve(runningStatus({ active_window_offset: 1 }));
    firstCrop.resolve({ ...defaultCropSettings, code: 'stale' });
    await firstStart;

    secondStatus.resolve(runningStatus({ active_window_offset: 2 }));
    secondCrop.resolve({ ...defaultCropSettings, code: 'current' });
    await secondStart;

    expect(workflow.getSnapshot().service.status?.active_window_offset).toBe(2);
    expect(workflow.getSnapshot().crop.code).toBe('current');
    expect(scheduler.intervals.size).toBe(1);
  });

  it('runs through generated/native-shaped and Preview command adapters', async () => {
    const nativeLike = {
      ensureStreamSession: vi.fn().mockResolvedValue(runningStatus()),
      getStreamStatus: vi.fn().mockResolvedValue(runningStatus()),
      restartStreamSession: vi.fn().mockResolvedValue(runningStatus()),
      setStreamWindow: vi.fn().mockResolvedValue(runningStatus()),
      getOverlaySettings: vi.fn().mockResolvedValue(defaultCropSettings),
      applyOverlayCropCode: vi.fn().mockResolvedValue(defaultCropSettings),
      saveOverlayDisplayMode: vi.fn().mockResolvedValue(defaultCropSettings),
      resetOverlayCrop: vi.fn().mockResolvedValue(defaultCropSettings)
    } satisfies Parameters<typeof createStreamCommandPort>[0];

    for (const commands of [
      createStreamCommandPort(nativeLike),
      createStreamCommandPort(commandClient)
    ]) {
      const workflow = createStreamWorkflow({
        commands,
        scheduler: new FakeScheduler(),
        clipboard: { writeText: async () => undefined },
        opener: { open: async () => undefined },
        currentLocale: () => 'zh'
      });
      await workflow.start();
      expect(workflow.getSnapshot().service.phase).not.toBe('loading');
      workflow.dispose();
    }
  });
});
