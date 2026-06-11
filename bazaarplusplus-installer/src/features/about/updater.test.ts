import { describe, expect, it } from 'vitest';
import type { DownloadEvent } from '@tauri-apps/plugin-updater';
import {
  createUpdaterMachine,
  initialUpdaterSnapshot,
  isUpdateModalPhase,
  type UpdateHandle,
  type UpdaterImpl
} from './updater';

function fakeUpdate(overrides: Partial<UpdateHandle> = {}): UpdateHandle {
  return {
    version: '9.9.9',
    body: 'release notes',
    downloadAndInstall: async () => undefined,
    close: async () => undefined,
    ...overrides
  };
}

function fakeImpl(overrides: Partial<UpdaterImpl> = {}): UpdaterImpl {
  return {
    check: async () => null,
    relaunch: async () => undefined,
    hasRuntime: () => true,
    isWindows: () => false,
    ...overrides
  };
}

function harness(impl: UpdaterImpl) {
  const phases: string[] = [];
  const machine = createUpdaterMachine(impl, (snapshot) => {
    phases.push(snapshot.phase);
  });
  return { machine, phases, snapshot: () => machine.getSnapshot() };
}

/** A downloadAndInstall the test drives event-by-event. */
function drivableDownload() {
  let emit: ((event: DownloadEvent) => void) | undefined;
  let finish: (() => void) | undefined;
  let fail: ((error: unknown) => void) | undefined;
  const downloadAndInstall: UpdateHandle['downloadAndInstall'] = (onEvent) => {
    emit = onEvent;
    return new Promise<void>((resolve, reject) => {
      finish = resolve;
      fail = reject;
    });
  };
  return {
    downloadAndInstall,
    emit: (event: DownloadEvent) => emit?.(event),
    finish: () => finish?.(),
    fail: (error: unknown) => fail?.(error)
  };
}

describe('createUpdaterMachine checkNow', () => {
  it('captures version, notes and the update handle when an update is available', async () => {
    const update = fakeUpdate({ version: '5.0.0', body: 'fixes' });
    const { machine, snapshot } = harness(
      fakeImpl({ check: async () => update })
    );

    await machine.checkNow();

    expect(snapshot().phase).toBe('available');
    expect(snapshot().version).toBe('5.0.0');
    expect(snapshot().notes).toBe('fixes');
  });

  it('reports current when no update is available', async () => {
    const { machine, phases, snapshot } = harness(fakeImpl());

    await machine.checkNow();

    expect(phases).toEqual(['checking', 'current']);
    expect(snapshot().error).toBeNull();
  });

  it('short-circuits to preview outside the Tauri runtime', async () => {
    let checked = false;
    const { machine, snapshot } = harness(
      fakeImpl({
        hasRuntime: () => false,
        check: async () => {
          checked = true;
          return null;
        }
      })
    );

    await machine.checkNow();

    expect(snapshot().phase).toBe('preview');
    expect(checked).toBe(false);
  });

  it('surfaces manual check failures as a check-sourced error', async () => {
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () => {
          throw new Error('endpoint unreachable');
        }
      })
    );

    await machine.checkNow();

    expect(snapshot().phase).toBe('error');
    expect(snapshot().errorSource).toBe('check');
    expect(snapshot().error).toBe('endpoint unreachable');
    expect(isUpdateModalPhase(snapshot())).toBe(false);
  });

  it('keeps silent startup checks quiet unless an update is available', async () => {
    const failing = harness(
      fakeImpl({
        check: async () => {
          throw new Error('offline');
        }
      })
    );
    await failing.machine.checkNow({ silent: true });
    expect(failing.snapshot()).toEqual(initialUpdaterSnapshot);
    expect(failing.phases).toEqual([]);

    const available = harness(fakeImpl({ check: async () => fakeUpdate() }));
    await available.machine.checkNow({ silent: true });
    expect(available.snapshot().phase).toBe('available');
  });
});

describe('createUpdaterMachine install', () => {
  it('walks downloading → installing → ready and accumulates progress', async () => {
    const download = drivableDownload();
    const update = fakeUpdate({
      downloadAndInstall: download.downloadAndInstall
    });
    const { machine, phases, snapshot } = harness(
      fakeImpl({ check: async () => update })
    );
    await machine.checkNow();

    const installed = machine.install();
    expect(snapshot().phase).toBe('downloading');

    download.emit({ event: 'Started', data: { contentLength: 100 } });
    expect(snapshot().progress).toEqual({ downloaded: 0, total: 100 });
    download.emit({ event: 'Progress', data: { chunkLength: 30 } });
    download.emit({ event: 'Progress', data: { chunkLength: 45 } });
    expect(snapshot().progress).toEqual({ downloaded: 75, total: 100 });

    download.emit({ event: 'Finished' });
    expect(snapshot().phase).toBe('installing');

    download.finish();
    await installed;
    expect(snapshot().phase).toBe('ready');
    expect(phases).toContain('downloading');
  });

  it('keeps an indeterminate total when Started has no contentLength', async () => {
    const download = drivableDownload();
    const update = fakeUpdate({
      downloadAndInstall: download.downloadAndInstall
    });
    const { machine, snapshot } = harness(
      fakeImpl({ check: async () => update })
    );
    await machine.checkNow();

    const installed = machine.install();
    download.emit({ event: 'Started', data: {} });
    download.emit({ event: 'Progress', data: { chunkLength: 10 } });
    expect(snapshot().progress).toEqual({ downloaded: 10, total: null });

    download.emit({ event: 'Finished' });
    download.finish();
    await installed;
  });

  it('relaunches automatically on Windows instead of waiting in ready', async () => {
    let relaunched = 0;
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () => fakeUpdate(),
        isWindows: () => true,
        relaunch: async () => {
          relaunched += 1;
        }
      })
    );
    await machine.checkNow();

    await machine.install();

    expect(relaunched).toBe(1);
    expect(snapshot().phase).not.toBe('ready');
  });

  it('moves to an install-sourced error and re-checks for a fresh handle on retry', async () => {
    let checks = 0;
    const brokenDownload = drivableDownload();
    const broken = fakeUpdate({
      downloadAndInstall: brokenDownload.downloadAndInstall
    });
    let healthyInstalls = 0;
    const healthy = fakeUpdate({
      downloadAndInstall: async () => {
        healthyInstalls += 1;
      }
    });
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () => {
          checks += 1;
          return checks === 1 ? broken : healthy;
        }
      })
    );

    await machine.checkNow();
    const failed = machine.install();
    brokenDownload.fail(new Error('signature mismatch'));
    await failed;

    expect(snapshot().phase).toBe('error');
    expect(snapshot().errorSource).toBe('install');
    expect(snapshot().error).toBe('signature mismatch');
    expect(isUpdateModalPhase(snapshot())).toBe(true);

    // Retry: the consumed handle must not be reused — a fresh check runs.
    await machine.install();
    expect(checks).toBe(2);
    expect(healthyInstalls).toBe(1);
    expect(snapshot().phase).toBe('ready');
  });

  it('falls back to the check outcome when retrying after the update disappeared', async () => {
    const download = drivableDownload();
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () =>
          fakeUpdate({ downloadAndInstall: download.downloadAndInstall })
      })
    );
    await machine.checkNow();
    const failed = machine.install();
    download.fail(new Error('boom'));
    await failed;

    const noUpdate = harness(fakeImpl({ check: async () => null }));
    await noUpdate.machine.install();
    expect(noUpdate.snapshot().phase).toBe('current');
    expect(snapshot().phase).toBe('error');
  });
});

describe('createUpdaterMachine guards and actions', () => {
  it('ignores checkNow and a second install while busy', async () => {
    let checks = 0;
    const download = drivableDownload();
    const update = fakeUpdate({
      downloadAndInstall: download.downloadAndInstall
    });
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () => {
          checks += 1;
          return update;
        }
      })
    );
    await machine.checkNow();

    const installed = machine.install();
    await machine.checkNow();
    await machine.install();
    machine.dismiss();

    expect(checks).toBe(1);
    expect(snapshot().phase).toBe('downloading');

    download.emit({ event: 'Finished' });
    download.finish();
    await installed;
  });

  it('dismiss returns to idle from a dismissable phase', async () => {
    const { machine, snapshot } = harness(
      fakeImpl({ check: async () => fakeUpdate() })
    );
    await machine.checkNow();
    expect(snapshot().phase).toBe('available');

    machine.dismiss();
    expect(snapshot().phase).toBe('idle');
    expect(isUpdateModalPhase(snapshot())).toBe(false);
  });

  it('restart relaunches and keeps the phase with an inline error on failure', async () => {
    let relaunched = 0;
    const ok = harness(
      fakeImpl({
        relaunch: async () => {
          relaunched += 1;
        }
      })
    );
    await ok.machine.restart();
    expect(relaunched).toBe(1);

    const failing = harness(
      fakeImpl({
        relaunch: async () => {
          throw new Error('spawn failed');
        }
      })
    );
    await failing.machine.restart();
    expect(failing.snapshot().error).toBe('spawn failed');
  });
});
