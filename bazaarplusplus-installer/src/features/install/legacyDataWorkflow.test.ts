// Covers what the Install page cannot show statically: deletion runs only
// after a confirmation fixed to one listed root, a refusal keeps the root and
// the dialog for retry, a slower stale load never overwrites newer data, and
// the V5 import runs, fails, or completes into an offer to delete V5 that the
// user may decline.
import { describe, expect, it, vi } from 'vitest';
import { SemanticProblemError } from '../../api/problems';
import type { LegacyDataState } from '../../types/backend';
import {
  createLegacyDataWorkflow,
  type LegacyDataCommandPort
} from './legacyDataWorkflow';

const GAME = '/Games/The Bazaar';

function state(
  names: LegacyDataState['roots'][number]['name'][],
  v5ImportEligible = false
) {
  return {
    game_path: GAME,
    roots: names.map((name) => ({
      name,
      size_bytes: 1024,
      file_count: 1,
      hard_linked_file_count: 0,
      unreadable_entry_count: 0
    })),
    installed_mod_data_root: 'current',
    v5_import_eligible: v5ImportEligible
  } satisfies LegacyDataState;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settle) => {
    resolve = settle;
  });
  return { promise, resolve: (value: T) => resolve(value) };
}

function commands(
  overrides: Partial<LegacyDataCommandPort> = {}
): LegacyDataCommandPort {
  return {
    getLegacyDataState: vi.fn(async () =>
      state(['BazaarPlusPlusV4', 'BazaarPlusPlusV5'])
    ),
    importV5History: vi.fn(async () => ({
      imported_run_count: 8,
      skipped_run_count: 1,
      unavailable_file_count: 2,
      state: state(['BazaarPlusPlusV4', 'BazaarPlusPlusV5'])
    })),
    deleteLegacyRoot: vi.fn(async () => ({
      removed: true,
      state: state(['BazaarPlusPlusV4'])
    })),
    ...overrides
  };
}

describe('legacy data workflow', () => {
  it('lists roots and never deletes without a confirmation', async () => {
    const port = commands();
    const workflow = createLegacyDataWorkflow({ commands: port });

    await workflow.load(GAME);

    expect(workflow.getSnapshot().load).toEqual({
      phase: 'ready',
      data: state(['BazaarPlusPlusV4', 'BazaarPlusPlusV5'])
    });
    expect(await workflow.confirmDelete()).toBe(false);
    expect(port.deleteLegacyRoot).not.toHaveBeenCalled();
  });

  it('deletes the confirmed root and installs the returned state', async () => {
    const port = commands();
    const workflow = createLegacyDataWorkflow({ commands: port });
    await workflow.load(GAME);

    expect(workflow.requestDelete('BazaarPlusPlusV5')).toBe(true);
    // A second request cannot retarget the open confirmation.
    expect(workflow.requestDelete('BazaarPlusPlusV4')).toBe(false);
    expect(await workflow.confirmDelete()).toBe(true);

    expect(port.deleteLegacyRoot).toHaveBeenCalledExactlyOnceWith(
      GAME,
      'BazaarPlusPlusV5'
    );
    const snapshot = workflow.getSnapshot();
    expect(snapshot.confirmation).toBeNull();
    expect(snapshot.load).toEqual({
      phase: 'ready',
      data: state(['BazaarPlusPlusV4'])
    });
    expect(snapshot.notice).toMatchObject({
      name: 'BazaarPlusPlusV5',
      removed: true
    });
    workflow.acknowledgeNotice(snapshot.notice!.id);
    expect(workflow.getSnapshot().notice).toBeNull();
  });

  it('refuses to request a root that is not listed', async () => {
    const workflow = createLegacyDataWorkflow({ commands: commands() });
    await workflow.load(GAME);

    expect(workflow.requestDelete('BazaarPlusPlus')).toBe(false);
    expect(workflow.getSnapshot().confirmation).toBeNull();
  });

  it('keeps the confirmation with its problem when the game blocks deletion', async () => {
    const port = commands({
      deleteLegacyRoot: vi.fn(async () => {
        throw new SemanticProblemError({
          code: 'legacy_delete_blocked_by_game',
          params: { operation: 'delete_legacy_root' },
          diagnostic: null
        });
      })
    });
    const workflow = createLegacyDataWorkflow({ commands: port });
    await workflow.load(GAME);
    workflow.requestDelete('BazaarPlusPlusV5');

    expect(await workflow.confirmDelete()).toBe(false);

    const snapshot = workflow.getSnapshot();
    expect(snapshot.confirmation).toMatchObject({
      phase: 'failed',
      target: { gamePath: GAME, name: 'BazaarPlusPlusV5' },
      problem: { code: 'legacy_delete_blocked_by_game' }
    });
    expect(snapshot.load).toMatchObject({
      phase: 'ready',
      data: state(['BazaarPlusPlusV4', 'BazaarPlusPlusV5'])
    });
    expect(snapshot.notice).toBeNull();
  });

  it('drops a load that settles after a newer one', async () => {
    const pending = deferred<LegacyDataState>();
    const port = commands({
      getLegacyDataState: vi
        .fn()
        .mockImplementationOnce(() => pending.promise)
        .mockImplementationOnce(async () => state(['BazaarPlusPlus']))
    });
    const workflow = createLegacyDataWorkflow({ commands: port });

    const first = workflow.load('/old');
    await workflow.load(GAME);
    pending.resolve(state(['BazaarPlusPlusV5']));
    await first;

    expect(workflow.getSnapshot().load).toEqual({
      phase: 'ready',
      data: state(['BazaarPlusPlus'])
    });
  });

  describe('V5 import', () => {
    const eligible = () => state(['BazaarPlusPlusV5'], true);

    it('runs only while eligible', async () => {
      const port = commands();
      const workflow = createLegacyDataWorkflow({ commands: port });
      await workflow.load(GAME);

      expect(await workflow.importV5()).toBe(false);
      expect(port.importV5History).not.toHaveBeenCalled();
    });

    it('blocks deletion while importing, then offers to delete V5', async () => {
      const pending = deferred<{
        imported_run_count: number;
        skipped_run_count: number;
        unavailable_file_count: number;
        state: LegacyDataState;
      }>();
      const port = commands({
        getLegacyDataState: vi.fn(async () => eligible()),
        importV5History: vi.fn(() => pending.promise)
      });
      const workflow = createLegacyDataWorkflow({ commands: port });
      await workflow.load(GAME);

      const running = workflow.importV5();
      expect(workflow.getSnapshot().importRun).toEqual({
        phase: 'running',
        gamePath: GAME
      });
      expect(workflow.requestDelete('BazaarPlusPlusV5')).toBe(false);
      expect(await workflow.importV5()).toBe(false);

      pending.resolve({
        imported_run_count: 8,
        skipped_run_count: 1,
        unavailable_file_count: 0,
        state: state(['BazaarPlusPlusV5'])
      });
      expect(await running).toBe(true);

      expect(port.importV5History).toHaveBeenCalledExactlyOnceWith(GAME);
      const snapshot = workflow.getSnapshot();
      expect(snapshot.importRun).toEqual({ phase: 'idle' });
      expect(snapshot.load).toEqual({
        phase: 'ready',
        data: state(['BazaarPlusPlusV5'])
      });
      expect(snapshot.confirmation).toEqual({
        phase: 'confirming',
        target: {
          gamePath: GAME,
          name: 'BazaarPlusPlusV5',
          afterImport: {
            imported_run_count: 8,
            skipped_run_count: 1,
            unavailable_file_count: 0
          }
        },
        problem: null
      });
      // Nothing is deleted until the user confirms the offer.
      expect(port.deleteLegacyRoot).not.toHaveBeenCalled();
    });

    it('keeps V5 listed when the user declines the offer', async () => {
      const port = commands({
        getLegacyDataState: vi.fn(async () => eligible())
      });
      const workflow = createLegacyDataWorkflow({ commands: port });
      await workflow.load(GAME);
      await workflow.importV5();

      expect(workflow.dismissDelete()).toBe(true);

      const snapshot = workflow.getSnapshot();
      expect(snapshot.confirmation).toBeNull();
      expect(port.deleteLegacyRoot).not.toHaveBeenCalled();
      expect(snapshot.load).toMatchObject({
        phase: 'ready',
        data: {
          roots: [{ name: 'BazaarPlusPlusV4' }, { name: 'BazaarPlusPlusV5' }]
        }
      });
      // A later delete of V5 is the ordinary confirmation, without the offer.
      expect(workflow.requestDelete('BazaarPlusPlusV5')).toBe(true);
      expect(workflow.getSnapshot().confirmation?.target).toEqual({
        gamePath: GAME,
        name: 'BazaarPlusPlusV5'
      });
    });

    it('deletes V5 when the user accepts the offer', async () => {
      const port = commands({
        getLegacyDataState: vi.fn(async () => eligible())
      });
      const workflow = createLegacyDataWorkflow({ commands: port });
      await workflow.load(GAME);
      await workflow.importV5();

      expect(await workflow.confirmDelete()).toBe(true);
      expect(port.deleteLegacyRoot).toHaveBeenCalledExactlyOnceWith(
        GAME,
        'BazaarPlusPlusV5'
      );
    });

    it('keeps a failed import as a typed problem and rereads the roots', async () => {
      const port = commands({
        getLegacyDataState: vi.fn(async () => eligible()),
        importV5History: vi.fn(async () => {
          throw new SemanticProblemError({
            code: 'import_failed',
            params: {
              operation: 'import_v5_history',
              reason: 'insufficient_space'
            },
            diagnostic: 'needs 2048 bytes'
          });
        })
      });
      const workflow = createLegacyDataWorkflow({ commands: port });
      await workflow.load(GAME);

      expect(await workflow.importV5()).toBe(false);

      const snapshot = workflow.getSnapshot();
      expect(snapshot.importRun).toMatchObject({
        phase: 'failed',
        problem: {
          code: 'import_failed',
          params: { reason: 'insufficient_space' }
        }
      });
      expect(snapshot.confirmation).toBeNull();
      expect(port.getLegacyDataState).toHaveBeenCalledTimes(2);
      expect(port.deleteLegacyRoot).not.toHaveBeenCalled();
    });

    it('reports a game that blocks the import with its own code', async () => {
      const port = commands({
        getLegacyDataState: vi.fn(async () => eligible()),
        importV5History: vi.fn(async () => {
          throw new SemanticProblemError({
            code: 'import_blocked_by_game',
            params: { operation: 'import_v5_history', stage: 'start' },
            diagnostic: null
          });
        })
      });
      const workflow = createLegacyDataWorkflow({ commands: port });
      await workflow.load(GAME);
      await workflow.importV5();

      expect(workflow.getSnapshot().importRun).toMatchObject({
        phase: 'failed',
        problem: { code: 'import_blocked_by_game' }
      });
    });
  });
});
