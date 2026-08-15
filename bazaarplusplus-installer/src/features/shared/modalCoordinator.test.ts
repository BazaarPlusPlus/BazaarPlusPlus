import { describe, expect, it, vi } from 'vitest';
import {
  createModalCoordinator,
  restoreModalFocus,
  type ModalPriority,
  type ModalRequest
} from './modalCoordinator';

function request(id: string, priority: ModalPriority): ModalRequest {
  return {
    id,
    priority,
    dismissalPolicy: priority === 'critical' ? 'blocked' : 'dismissible'
  };
}

describe('modal coordinator', () => {
  it('uses critical, confirmation, system, then informational priority', () => {
    const coordinator = createModalCoordinator();

    coordinator.register(request('payment', 'informational'));
    expect(coordinator.getSnapshot().active?.id).toBe('payment');

    coordinator.register(request('update', 'system'));
    expect(coordinator.getSnapshot().active?.id).toBe('update');

    coordinator.register(request('reset', 'confirmation'));
    expect(coordinator.getSnapshot().active?.id).toBe('reset');

    coordinator.register(request('delete-running', 'critical'));
    expect(coordinator.getSnapshot().active?.id).toBe('delete-running');
  });

  it('does not let update or support requests interrupt an active confirmation', () => {
    const coordinator = createModalCoordinator();
    coordinator.register(request('cleanup', 'confirmation'));
    coordinator.register(request('update', 'system'));
    coordinator.register(request('payment', 'informational'));

    expect(coordinator.getSnapshot().active?.id).toBe('cleanup');
    expect(coordinator.getSnapshot().queued.map(({ id }) => id)).toEqual([
      'update',
      'payment'
    ]);
  });

  it('keeps equal priorities FIFO and safely removes a queued source', () => {
    const coordinator = createModalCoordinator();
    coordinator.register(request('first-reset', 'confirmation'));
    coordinator.register(request('second-delete', 'confirmation'));
    coordinator.register(request('third-cleanup', 'confirmation'));

    coordinator.unregister('second-delete');
    coordinator.unregister('first-reset');

    expect(coordinator.getSnapshot().active?.id).toBe('third-cleanup');
    expect(coordinator.getSnapshot().queued).toEqual([]);
  });

  it('retains an active confirmation when it adopts critical blocked semantics', () => {
    const coordinator = createModalCoordinator();
    coordinator.register(request('reset', 'confirmation'));
    coordinator.register(request('other-confirmation', 'confirmation'));

    coordinator.update('reset', {
      priority: 'critical',
      dismissalPolicy: 'blocked'
    });

    expect(coordinator.getSnapshot().active).toMatchObject({
      id: 'reset',
      priority: 'critical',
      dismissalPolicy: 'blocked'
    });
    expect(coordinator.getSnapshot().queued.map(({ id }) => id)).toEqual([
      'other-confirmation'
    ]);
  });

  it('returns to a preempted source after critical work unregisters', () => {
    const coordinator = createModalCoordinator();
    coordinator.register(request('update', 'system'));
    coordinator.register(request('reset', 'confirmation'));
    coordinator.register(request('native-critical', 'critical'));

    expect(coordinator.getSnapshot().active?.id).toBe('native-critical');
    coordinator.unregister('native-critical');
    expect(coordinator.getSnapshot().active?.id).toBe('reset');
  });
});

describe('modal focus restoration', () => {
  it('prefers a still-connected trigger', () => {
    const trigger = { isConnected: true, focus: vi.fn() };
    const heading = { isConnected: true, focus: vi.fn() };

    expect(restoreModalFocus(trigger, [heading])).toBe(trigger);
    expect(trigger.focus).toHaveBeenCalledOnce();
    expect(heading.focus).not.toHaveBeenCalled();
  });

  it('falls back to the first connected page target after route unmount', () => {
    const staleTrigger = { isConnected: false, focus: vi.fn() };
    const staleHeading = { isConnected: false, focus: vi.fn() };
    const main = { isConnected: true, focus: vi.fn() };

    expect(restoreModalFocus(staleTrigger, [staleHeading, main])).toBe(main);
    expect(main.focus).toHaveBeenCalledOnce();
  });
});
