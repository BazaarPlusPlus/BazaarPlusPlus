// Isolated on purpose: the Stream page's rendered states (idle, running,
// runtime error) are anchored by src/shell.snapshot.test.tsx. These cases cover
// polling and settle order a static snapshot cannot observe.
// - An older poll overwrites a newer status, a slow poll overwrites a completed
//   restart, or an older failed poll marks a newer status stale.
// - A poll that only changes the reported error is not published.
// - A runtime error is reported as polling staleness, or the reverse.
// - A semantic notice persists, or a one-off failure leaks to other targets.
// - Responses or timers fire after dispose, or a dispose-start replay accepts
//   the old initialization.
import { describe, expect, it, vi } from 'vitest';
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
    db: { found: true },
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
    for (const callback of Array.from(this.intervals.values())) callback();
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
    ensureStreamSession: vi.fn().mockResolvedValue(runningStatus()),
    getStreamStatus: vi.fn().mockResolvedValue(runningStatus()),
    restartStreamSession: vi.fn().mockResolvedValue(runningStatus()),
    setStreamWindow: vi.fn().mockResolvedValue(runningStatus()),
    getOverlaySettings: vi.fn().mockResolvedValue(defaultCropSettings),
    applyOverlayCropCode: vi.fn().mockResolvedValue(defaultCropSettings),
    saveOverlayDisplayMode: vi.fn().mockResolvedValue(defaultCropSettings),
    resetOverlayCrop: vi.fn().mockResolvedValue(defaultCropSettings),
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
    const getStreamStatus = vi
      .fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);
    const { workflow, scheduler } = setup({ getStreamStatus });
    await workflow.start();

    scheduler.fireIntervals();
    scheduler.fireIntervals();
    second.resolve(runningStatus({ active_window_offset: 2 }));
    await flush();
    first.resolve(runningStatus({ active_window_offset: 1 }));
    await flush();

    expect(workflow.getSnapshot().service.status?.active_window_offset).toBe(2);
  });

  it('still publishes when a poll changes the reported error', async () => {
    const getStreamStatus = vi
      .fn()
      .mockResolvedValueOnce(runningStatus())
      .mockResolvedValue(runningStatus({ last_error: 'overlay port lost' }));
    const { workflow, scheduler } = setup({ getStreamStatus });
    await workflow.start();

    scheduler.fireIntervals();
    await flush();
    const before = workflow.getSnapshot();

    scheduler.fireIntervals();
    await flush();

    expect(workflow.getSnapshot()).not.toBe(before);
    expect(workflow.getSnapshot().service.status?.last_error).toBe(
      'overlay port lost'
    );
  });

  it('does not mark a newer status stale when an older poll fails', async () => {
    const first = deferred<StreamServiceStatus>();
    const second = deferred<StreamServiceStatus>();
    const getStreamStatus = vi
      .fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);
    const { workflow, scheduler } = setup({ getStreamStatus });
    await workflow.start();

    scheduler.fireIntervals();
    scheduler.fireIntervals();
    second.resolve(runningStatus({ active_window_offset: 2 }));
    await flush();
    first.reject(new Error('outdated failure'));
    await flush();

    expect(workflow.getSnapshot().polling).toMatchObject({
      freshness: 'fresh',
      problem: null
    });
  });

  it('does not let a slow poll overwrite a completed restart', async () => {
    const slowPoll = deferred<StreamServiceStatus>();
    const refreshed = runningStatus({ active_window_offset: 3 });
    const { workflow, scheduler } = setup({
      getStreamStatus: vi.fn(() => slowPoll.promise),
      restartStreamSession: vi.fn().mockResolvedValue(refreshed)
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
      ensureStreamSession: vi.fn().mockResolvedValue(failedStatus)
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
    const { workflow, scheduler } = setup({
      getStreamStatus: () => slow.promise
    });
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
    const ensureStreamSession = vi
      .fn()
      .mockImplementationOnce(() => firstStatus.promise)
      .mockImplementationOnce(() => secondStatus.promise);
    const getOverlaySettings = vi
      .fn()
      .mockImplementationOnce(() => firstCrop.promise)
      .mockImplementationOnce(() => secondCrop.promise);
    const { workflow, scheduler } = setup({
      ensureStreamSession,
      getOverlaySettings
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
});
