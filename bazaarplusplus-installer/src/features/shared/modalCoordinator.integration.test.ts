import { describe, expect, it } from 'vitest';
import { createModalCoordinator } from './modalCoordinator';

describe('shell and routed modal integration', () => {
  it('queues background shell sources behind a routed confirmation across source unmounts', () => {
    const coordinator = createModalCoordinator();
    coordinator.register({
      id: 'route:delete-video',
      priority: 'confirmation',
      dismissalPolicy: 'dismissible'
    });
    coordinator.register({
      id: 'shell:update',
      priority: 'system',
      dismissalPolicy: 'dismissible'
    });
    coordinator.register({
      id: 'shell:payment',
      priority: 'informational',
      dismissalPolicy: 'dismissible'
    });

    expect(coordinator.getSnapshot().active?.id).toBe('route:delete-video');

    coordinator.update('route:delete-video', {
      priority: 'critical',
      dismissalPolicy: 'blocked'
    });
    expect(coordinator.getSnapshot().active).toMatchObject({
      id: 'route:delete-video',
      priority: 'critical',
      dismissalPolicy: 'blocked'
    });

    coordinator.unregister('route:delete-video');
    expect(coordinator.getSnapshot().active?.id).toBe('shell:update');

    coordinator.unregister('shell:update');
    expect(coordinator.getSnapshot().active?.id).toBe('shell:payment');
  });
});
