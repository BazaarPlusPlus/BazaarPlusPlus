// Covers what the Install page cannot show statically: deletion runs only
// after a confirmation fixed to one listed root, a refusal keeps the root and
// the dialog for retry, and a slower stale load never overwrites newer data.
import { describe, expect, it, vi } from 'vitest';
import { SemanticProblemError } from '../../api/problems';
import type { LegacyDataState } from '../../types/backend';
import {
  createLegacyDataWorkflow,
  type LegacyDataCommandPort
} from './legacyDataWorkflow';

const GAME = '/Games/The Bazaar';

function state(names: LegacyDataState['roots'][number]['name'][]) {
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
    v5_import_eligible: false
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
});
