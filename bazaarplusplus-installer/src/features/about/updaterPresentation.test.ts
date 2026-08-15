import { describe, expect, it } from 'vitest';
import { createModalCoordinator } from '../shared/modalCoordinator';
import { createUiProblem } from '../shared/problems';
import type { UpdaterSnapshot } from './updater';
import { getUpdaterUiContract } from './updaterPresentation';

const available: UpdaterSnapshot = {
  phase: 'available',
  version: '5.1.0',
  notes: 'notes',
  progress: null,
  problem: null
};

describe('updater UI contract', () => {
  it('derives non-conflicting header and modal behavior from each explicit phase', () => {
    const downloading: UpdaterSnapshot = {
      ...available,
      phase: 'downloading',
      progress: { downloaded: 25, total: 100 }
    };
    const installing: UpdaterSnapshot = {
      ...available,
      phase: 'installing'
    };
    const ready: UpdaterSnapshot = {
      ...available,
      phase: 'ready-to-restart'
    };
    const restarting: UpdaterSnapshot = {
      ...available,
      phase: 'restarting'
    };

    expect(getUpdaterUiContract(available)).toMatchObject({
      header: { labelKey: 'updateHeaderAvailable', disabled: true },
      modal: {
        action: 'install',
        priority: 'system',
        dismissalPolicy: 'dismissible'
      }
    });
    expect(getUpdaterUiContract(downloading)).toMatchObject({
      header: { labelKey: 'updateDownloading', busy: true },
      modal: {
        action: null,
        priority: 'critical',
        dismissalPolicy: 'blocked'
      }
    });
    expect(getUpdaterUiContract(installing).modal).toMatchObject({
      action: null,
      dismissalPolicy: 'blocked'
    });
    expect(getUpdaterUiContract(ready).modal).toMatchObject({
      action: 'restart',
      dismissalPolicy: 'dismissible'
    });
    expect(getUpdaterUiContract(restarting)).toMatchObject({
      header: { labelKey: 'updateRestarting', busy: true },
      modal: { action: null, dismissalPolicy: 'blocked' }
    });
  });

  it('keeps check failure in the shell and gives modal failures the correct retry', () => {
    const checkFailed: UpdaterSnapshot = {
      phase: 'failed',
      version: null,
      notes: null,
      progress: null,
      problem: createUiProblem('updater_check_failed', {
        params: { operation: 'check' }
      })
    };
    const restartFailed: UpdaterSnapshot = {
      ...available,
      phase: 'failed',
      progress: null,
      problem: createUiProblem('updater_restart_failed', {
        params: { operation: 'restart', version: '5.1.0' }
      })
    };

    expect(getUpdaterUiContract(checkFailed)).toMatchObject({
      header: { labelKey: 'headerCheckFailed', disabled: false },
      modal: null
    });
    expect(getUpdaterUiContract(restartFailed).modal).toMatchObject({
      action: 'retry-restart',
      dismissalPolicy: 'dismissible'
    });
  });

  it('queues system update decisions behind an active confirmation', () => {
    const coordinator = createModalCoordinator();
    coordinator.register({
      id: 'route:reset',
      priority: 'confirmation',
      dismissalPolicy: 'dismissible'
    });
    const modal = getUpdaterUiContract(available).modal;
    if (!modal) throw new Error('available must have a modal contract');
    coordinator.register({
      id: 'shell:update',
      priority: modal.priority,
      dismissalPolicy: modal.dismissalPolicy
    });

    expect(coordinator.getSnapshot().active?.id).toBe('route:reset');
    expect(coordinator.getSnapshot().queued[0]?.id).toBe('shell:update');
  });
});
