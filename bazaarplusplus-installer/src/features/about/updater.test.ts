import type { DownloadEvent } from '@tauri-apps/plugin-updater';
import { describe, expect, it } from 'vitest';
import {
  createUpdaterMachine,
  initialUpdaterSnapshot,
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
  it('publishes one consistent available snapshot with version and notes', async () => {
    const update = fakeUpdate({ version: '5.0.0', body: 'fixes' });
    const { machine, snapshot } = harness(
      fakeImpl({ check: async () => update })
    );

    await machine.checkNow();

    expect(snapshot()).toEqual({
      phase: 'available',
      version: '5.0.0',
      notes: 'fixes',
      progress: null,
      problem: null
    });
  });

  it('reports checking then current when no update is available', async () => {
    const { machine, phases, snapshot } = harness(fakeImpl());

    await machine.checkNow();

    expect(phases).toEqual(['checking', 'current']);
    expect(snapshot().problem).toBeNull();
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

  it('classifies manual check failures without exposing diagnostics as state copy', async () => {
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () => {
          throw new Error('endpoint unreachable');
        }
      })
    );

    await machine.checkNow();

    expect(snapshot().phase).toBe('failed');
    expect(snapshot().problem).toMatchObject({
      code: 'updater_check_failed',
      params: { operation: 'check' },
      diagnostic: 'endpoint unreachable'
    });
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
  it('walks downloading → installing → ready-to-restart and accumulates progress', async () => {
    const download = drivableDownload();
    const { machine, phases, snapshot } = harness(
      fakeImpl({
        check: async () =>
          fakeUpdate({ downloadAndInstall: download.downloadAndInstall })
      })
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
    expect(snapshot().phase).toBe('ready-to-restart');
    expect(snapshot().version).toBe('9.9.9');
    expect(phases).toEqual(
      expect.arrayContaining(['downloading', 'installing', 'ready-to-restart'])
    );
  });

  it('keeps an indeterminate total when Started has no contentLength', async () => {
    const download = drivableDownload();
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () =>
          fakeUpdate({ downloadAndInstall: download.downloadAndInstall })
      })
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

  it('classifies a failure before Finished as download failure and retries with a fresh handle', async () => {
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

    expect(snapshot().phase).toBe('failed');
    expect(snapshot().problem).toMatchObject({
      code: 'updater_download_failed',
      params: { operation: 'download', version: '9.9.9' },
      diagnostic: 'signature mismatch'
    });

    await machine.install();
    expect(checks).toBe(2);
    expect(healthyInstalls).toBe(1);
    expect(snapshot().phase).toBe('ready-to-restart');
  });

  it('classifies a failure after Finished as install failure', async () => {
    const download = drivableDownload();
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () =>
          fakeUpdate({ downloadAndInstall: download.downloadAndInstall })
      })
    );
    await machine.checkNow();

    const failed = machine.install();
    download.emit({ event: 'Finished' });
    download.fail(new Error('installer rejected package'));
    await failed;

    expect(snapshot().phase).toBe('failed');
    expect(snapshot().problem).toMatchObject({
      code: 'updater_install_failed',
      params: { operation: 'install', version: '9.9.9' },
      diagnostic: 'installer rejected package'
    });
  });

  it('falls back to current when retrying after the update disappeared', async () => {
    let checks = 0;
    const download = drivableDownload();
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () => {
          checks += 1;
          return checks === 1
            ? fakeUpdate({ downloadAndInstall: download.downloadAndInstall })
            : null;
        }
      })
    );
    await machine.checkNow();
    const failed = machine.install();
    download.fail(new Error('boom'));
    await failed;

    await machine.install();
    expect(snapshot().phase).toBe('current');
  });
});

describe('createUpdaterMachine restart, dismissal, and guards', () => {
  it('publishes restarting before relaunch and retains the installed result on failure', async () => {
    let attempts = 0;
    const { machine, phases, snapshot } = harness(
      fakeImpl({
        check: async () => fakeUpdate({ version: '5.1.0' }),
        relaunch: async () => {
          attempts += 1;
          if (attempts === 1) throw new Error('spawn failed');
        }
      })
    );
    await machine.checkNow();
    await machine.install();
    expect(snapshot().phase).toBe('ready-to-restart');

    await machine.restart();
    expect(phases).toContain('restarting');
    expect(snapshot().phase).toBe('failed');
    expect(snapshot().version).toBe('5.1.0');
    expect(snapshot().problem).toMatchObject({
      code: 'updater_restart_failed',
      params: { operation: 'restart', version: '5.1.0' },
      diagnostic: 'spawn failed'
    });

    await machine.restart();
    expect(attempts).toBe(2);
    expect(snapshot().phase).toBe('restarting');
  });

  it('blocks check, duplicate install, and dismissal during native work', async () => {
    let checks = 0;
    const download = drivableDownload();
    const { machine, snapshot } = harness(
      fakeImpl({
        check: async () => {
          checks += 1;
          return fakeUpdate({
            downloadAndInstall: download.downloadAndInstall
          });
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

  it('dismisses available and failed phases back to a clean idle snapshot', async () => {
    const available = harness(fakeImpl({ check: async () => fakeUpdate() }));
    await available.machine.checkNow();
    available.machine.dismiss();
    expect(available.snapshot()).toEqual(initialUpdaterSnapshot);

    const failed = harness(
      fakeImpl({
        check: async () => {
          throw new Error('offline');
        }
      })
    );
    await failed.machine.checkNow();
    failed.machine.dismiss();
    expect(failed.snapshot()).toEqual(initialUpdaterSnapshot);
  });
});
