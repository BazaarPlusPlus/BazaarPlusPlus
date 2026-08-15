import { describe, expect, it, vi } from 'vitest';
import {
  createConfirmedOperationController,
  type ConfirmedOperationOutcome
} from './confirmedOperation';

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

describe('confirmed operation controller', () => {
  it('keeps a non-cancelable target visible, blocks dismissal and repeated submission, then closes on success', async () => {
    const controller = createConfirmedOperationController<
      { kind: 'cleanup'; scope: 'screenshots' },
      string
    >();
    const completion = deferred<ConfirmedOperationOutcome<string>>();
    const execute = vi.fn(() => completion.promise);

    expect(controller.request({ kind: 'cleanup', scope: 'screenshots' })).toBe(
      true
    );
    const first = controller.run(execute, String);

    expect(controller.getSnapshot()).toMatchObject({
      phase: 'running',
      target: { kind: 'cleanup', scope: 'screenshots' }
    });
    expect(controller.dismiss()).toBe(false);
    await expect(controller.run(execute, String)).resolves.toBe(false);
    expect(execute).toHaveBeenCalledTimes(1);

    completion.resolve({ ok: true });
    await expect(first).resolves.toBe(true);
    expect(controller.getSnapshot()).toBeNull();
  });

  it('keeps target and problem on failure, supports retry, and allows a safe exit', async () => {
    const controller = createConfirmedOperationController<
      { kind: 'delete-video'; battleId: string },
      string
    >();
    controller.request({ kind: 'delete-video', battleId: 'battle-7' });

    await expect(
      controller.run(
        async () => ({ ok: false, problem: 'delete failed' }),
        String
      )
    ).resolves.toBe(false);
    expect(controller.getSnapshot()).toEqual({
      phase: 'failed',
      target: { kind: 'delete-video', battleId: 'battle-7' },
      problem: 'delete failed'
    });

    await expect(
      controller.run(async () => ({ ok: true }), String)
    ).resolves.toBe(true);
    expect(controller.getSnapshot()).toBeNull();

    controller.request({ kind: 'delete-video', battleId: 'battle-8' });
    expect(controller.dismiss()).toBe(true);
    expect(controller.getSnapshot()).toBeNull();
  });

  it('allows pending target updates only while confirming', () => {
    const controller = createConfirmedOperationController<
      { kind: 'install'; path: string; acknowledged: boolean },
      string
    >();
    controller.request({
      kind: 'install',
      path: '/game',
      acknowledged: false
    });
    expect(
      controller.updateTarget({
        kind: 'install',
        path: '/game',
        acknowledged: true
      })
    ).toBe(true);
    expect(controller.getSnapshot()).toMatchObject({
      phase: 'confirming',
      target: { acknowledged: true }
    });

    void controller.run(async () => {
      expect(
        controller.updateTarget({
          kind: 'install',
          path: '/game',
          acknowledged: false
        })
      ).toBe(false);
      return { ok: true };
    }, String);
  });

  it.each([
    ['cleanup', { kind: 'cleanup', target: 'run_data:before_this_month' }],
    ['reset', { kind: 'reset-data', target: '/game/BazaarPlusPlusV5' }],
    ['delete', { kind: 'delete-video', target: 'battle-2/video-3' }]
  ])(
    '%s feature uses the shared success/failure lifecycle',
    async (_, target) => {
      const controller = createConfirmedOperationController<
        { kind: string; target: string },
        string
      >();
      controller.request(target);
      await controller.run(
        async () => ({ ok: false, problem: 'failed' }),
        String
      );
      expect(controller.getSnapshot()).toEqual({
        phase: 'failed',
        target,
        problem: 'failed'
      });
      await controller.run(async () => ({ ok: true }), String);
      expect(controller.getSnapshot()).toBeNull();
    }
  );
});
