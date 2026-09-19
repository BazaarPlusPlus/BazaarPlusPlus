import { describe, expect, it, vi } from 'vitest';
import type { HistoryRunDetail } from '../../types/backend';
import { createConfirmedOperationController } from '../shared/confirmedOperation';
import {
  createRunDetailWorkflow,
  type RunDetailCommands
} from './runDetailWorkflow';
import {
  runDetailProblemFromError,
  type RunDetailProblem
} from './runDetailProblems';

const detail: HistoryRunDetail = {
  run: {
    run_id: 'run-1',
    hero: 'Vanessa',
    game_mode: 'Ranked',
    started_at_utc: '2026-01-02T15:04:00Z',
    ended_at_utc: '2026-01-02T15:34:00Z',
    status: 'completed',
    result: 'win',
    victories: 10,
    losses: 2,
    final_day: 12,
    final_player_rank: 'Gold',
    final_player_rating: 1234,
    screenshot_id: 'shot-1',
    strip_url: null,
    player_name: 'Player'
  },
  battles: ['battle-1', 'battle-2'].map((battle_id) => ({
    battle_id,
    day: 1,
    hour: 2,
    result: 'win',
    opponent_hero: 'Mak',
    opponent_name: null,
    opponent_rank: null,
    opponent_rating: null,
    video: {
      video_id: `video-${battle_id}`,
      status: 'completed',
      file_size_bytes: 10,
      duration_ms: 100
    }
  }))
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}

function setup(
  overrides: Partial<RunDetailCommands> = {},
  runId: string | undefined = 'run-1'
) {
  const commands = {
    loadHistoryRunDetail: vi.fn().mockResolvedValue(detail),
    revealRunScreenshot: vi.fn().mockResolvedValue(undefined),
    revealBattleVideo: vi.fn().mockResolvedValue(undefined),
    deleteBattleVideo: vi.fn().mockResolvedValue(detail),
    ...overrides
  };
  const workflow = createRunDetailWorkflow(runId, commands);
  return { workflow, commands };
}

describe('Run Detail workflow', () => {
  it('loads nullable detail, reports blocking failure and recovers through refresh', async () => {
    const loadHistoryRunDetail = vi
      .fn()
      .mockResolvedValueOnce(null)
      .mockRejectedValueOnce(new Error('read failed'))
      .mockResolvedValueOnce(detail);
    const { workflow } = setup({ loadHistoryRunDetail });
    expect(workflow.getSnapshot().state.phase).toBe('initial-loading');
    await workflow.start();
    expect(workflow.getSnapshot().state.phase).toBe('not-found');
    expect(await workflow.intents.refresh()).toBe(false);
    expect(workflow.getSnapshot().state).toMatchObject({
      phase: 'blocking-failure',
      problem: { diagnostic: 'read failed' }
    });
    expect(await workflow.intents.refresh()).toBe(true);
    expect(workflow.getSnapshot().detail).toEqual(detail);
    expect(loadHistoryRunDetail).toHaveBeenCalledWith('run-1');
  });

  it('keeps a missing run id in not-found without dispatching a command', async () => {
    const commands = {
      loadHistoryRunDetail: vi.fn(),
      revealRunScreenshot: vi.fn(),
      revealBattleVideo: vi.fn(),
      deleteBattleVideo: vi.fn()
    };
    const workflow = createRunDetailWorkflow(undefined, commands);
    await workflow.start();
    expect(workflow.getSnapshot().state.phase).toBe('not-found');
    expect(commands.loadHistoryRunDetail).not.toHaveBeenCalled();
    expect(await workflow.intents.revealScreenshot()).toBe(false);
    expect((await workflow.intents.deleteVideo('battle-1', 'v')).ok).toBe(
      false
    );
    expect(commands.deleteBattleVideo).not.toHaveBeenCalled();
  });

  it('retains data on refresh failure and excludes actions until refresh finishes', async () => {
    const refresh = deferred<HistoryRunDetail>();
    const { workflow, commands } = setup({
      loadHistoryRunDetail: vi
        .fn()
        .mockResolvedValueOnce(detail)
        .mockReturnValueOnce(refresh.promise)
        .mockResolvedValue(detail)
    });
    await workflow.start();
    const loading = workflow.intents.refresh();
    expect(workflow.getSnapshot()).toMatchObject({
      detail,
      busy: true,
      refreshing: true
    });
    expect(await workflow.intents.revealScreenshot()).toBe(false);
    expect(await workflow.intents.revealVideo('battle-1', 'v')).toBe(false);
    expect((await workflow.intents.deleteVideo('battle-1', 'v')).ok).toBe(
      false
    );
    expect(commands.revealRunScreenshot).not.toHaveBeenCalled();
    expect(commands.revealBattleVideo).not.toHaveBeenCalled();
    expect(commands.deleteBattleVideo).not.toHaveBeenCalled();
    refresh.reject(new Error('database locked'));
    await loading;
    expect(workflow.getSnapshot().state).toMatchObject({
      phase: 'ready',
      data: detail,
      refresh: { phase: 'failed', problem: { diagnostic: 'database locked' } }
    });
    expect(await workflow.intents.refresh()).toBe(true);
    expect(workflow.getSnapshot().state).toMatchObject({
      phase: 'ready',
      refresh: { phase: 'idle' }
    });
  });

  it.each(['resolve', 'reject'] as const)(
    'ignores an older load that will %s without releasing the latest load',
    async (outcome) => {
      const old = deferred<HistoryRunDetail>();
      const latest = deferred<HistoryRunDetail>();
      const { workflow, commands } = setup({
        loadHistoryRunDetail: vi
          .fn()
          .mockReturnValueOnce(old.promise)
          .mockReturnValueOnce(latest.promise)
      });
      const first = workflow.start();
      const second = workflow.intents.refresh();
      if (outcome === 'resolve') old.resolve(detail);
      else old.reject(new Error('old failure'));
      await first;
      expect(workflow.getSnapshot().state.phase).toBe('initial-loading');
      expect(await workflow.intents.revealVideo('battle-1', 'v')).toBe(false);
      expect(commands.revealBattleVideo).not.toHaveBeenCalled();
      const newer = { ...detail, run: { ...detail.run, hero: 'Mak' } };
      latest.resolve(newer);
      await second;
      expect(workflow.getSnapshot()).toMatchObject({
        detail: newer,
        busy: false
      });
    }
  );

  it('does not overwrite a newer result when an old load completes last', async () => {
    const old = deferred<HistoryRunDetail>();
    const { workflow } = setup({
      loadHistoryRunDetail: vi
        .fn()
        .mockReturnValueOnce(old.promise)
        .mockResolvedValueOnce(detail)
    });
    const first = workflow.start();
    await workflow.intents.refresh();
    const snapshot = workflow.getSnapshot();
    old.resolve({ ...detail, battles: [] });
    await first;
    expect(workflow.getSnapshot()).toBe(snapshot);
  });

  it.each(['screenshot', 'reveal', 'delete'] as const)(
    'holds the shared slot throughout %s and preserves its target',
    async (kind) => {
      const action = deferred<void>();
      const { workflow, commands } = setup({
        revealRunScreenshot: vi.fn().mockReturnValue(action.promise),
        revealBattleVideo: vi.fn().mockReturnValue(action.promise),
        deleteBattleVideo: vi
          .fn()
          .mockReturnValue(action.promise.then(() => detail))
      });
      await workflow.start();
      const running =
        kind === 'screenshot'
          ? workflow.intents.revealScreenshot()
          : kind === 'reveal'
            ? workflow.intents.revealVideo('battle-1', 'video-battle-1')
            : workflow.intents.deleteVideo('battle-1', 'video-battle-1');
      expect(workflow.getSnapshot().busy).toBe(true);
      expect(workflow.getSnapshot().screenshot.disabled).toBe(true);
      expect(workflow.getSnapshot().battles['battle-2'].reveal.disabled).toBe(
        true
      );
      expect(await workflow.intents.refresh()).toBe(false);
      expect(await workflow.intents.revealScreenshot()).toBe(false);
      expect(await workflow.intents.revealVideo('battle-2', 'v')).toBe(false);
      expect((await workflow.intents.deleteVideo('battle-2', 'v')).ok).toBe(
        false
      );
      expect(commands.loadHistoryRunDetail).toHaveBeenCalledTimes(1);
      expect(commands.revealRunScreenshot).toHaveBeenCalledTimes(
        kind === 'screenshot' ? 1 : 0
      );
      expect(commands.revealBattleVideo).toHaveBeenCalledTimes(
        kind === 'reveal' ? 1 : 0
      );
      expect(commands.deleteBattleVideo).toHaveBeenCalledTimes(
        kind === 'delete' ? 1 : 0
      );
      if (kind === 'screenshot')
        expect(commands.revealRunScreenshot).toHaveBeenCalledWith('run-1');
      else
        expect(
          kind === 'delete'
            ? commands.deleteBattleVideo
            : commands.revealBattleVideo
        ).toHaveBeenCalledWith('battle-1', 'video-battle-1');
      action.resolve();
      await running;
      expect(workflow.getSnapshot().busy).toBe(false);
    }
  );

  it('attaches action failures to their target and clears only the target being retried', async () => {
    const retry = deferred<void>();
    const { workflow } = setup({
      revealRunScreenshot: vi
        .fn()
        .mockRejectedValue(new Error('screenshot failed')),
      revealBattleVideo: vi
        .fn()
        .mockRejectedValueOnce(new Error('video failed'))
        .mockReturnValueOnce(retry.promise)
    });
    await workflow.start();
    await workflow.intents.revealScreenshot();
    expect(await workflow.intents.revealVideo('battle-1', 'v')).toBe(false);
    expect(workflow.getSnapshot().battles['battle-1'].failure).toMatchObject({
      action: 'reveal',
      problem: { diagnostic: 'video failed' }
    });
    expect(workflow.getSnapshot().battles['battle-2'].failure).toBeNull();
    const retrying = workflow.intents.revealVideo('battle-1', 'v');
    expect(workflow.getSnapshot().battles['battle-1'].failure).toBeNull();
    expect(workflow.getSnapshot().screenshot.problem?.diagnostic).toBe(
      'screenshot failed'
    );
    retry.resolve();
    expect(await retrying).toBe(true);
    await workflow.intents.refresh();
    expect(workflow.getSnapshot().screenshot.problem).toBeNull();
  });

  it('keeps a failed deletion confirmation retryable and replaces detail after success', async () => {
    const deletion = deferred<HistoryRunDetail>();
    const { workflow, commands } = setup({
      deleteBattleVideo: vi
        .fn()
        .mockRejectedValueOnce(new Error('delete failed'))
        .mockReturnValueOnce(deletion.promise)
    });
    await workflow.start();
    const confirmation = createConfirmedOperationController<
      { battleId: string; videoId: string },
      RunDetailProblem
    >();
    const target = { battleId: 'battle-1', videoId: 'video-battle-1' };
    confirmation.request(target);
    const execute = () =>
      confirmation.run(
        (fixed) => workflow.intents.deleteVideo(fixed.battleId, fixed.videoId),
        runDetailProblemFromError
      );
    expect(await execute()).toBe(false);
    expect(confirmation.getSnapshot()).toMatchObject({
      phase: 'failed',
      target
    });
    expect(workflow.getSnapshot().battles['battle-1'].failure?.action).toBe(
      'delete'
    );
    const retry = execute();
    expect(confirmation.dismiss()).toBe(false);
    expect(confirmation.getSnapshot()).toMatchObject({
      phase: 'running',
      target
    });
    const updated = {
      ...detail,
      battles: detail.battles.map((battle) => ({ ...battle, video: null }))
    };
    deletion.resolve(updated);
    expect(await retry).toBe(true);
    expect(confirmation.getSnapshot()).toBeNull();
    expect(workflow.getSnapshot().detail).toEqual(updated);
    expect(commands.deleteBattleVideo).toHaveBeenLastCalledWith(
      target.battleId,
      target.videoId
    );
  });

  it('invalidates late loads on dispose and survives a new lifecycle', async () => {
    const old = deferred<HistoryRunDetail>();
    const { workflow } = setup({
      loadHistoryRunDetail: vi
        .fn()
        .mockReturnValueOnce(old.promise)
        .mockResolvedValueOnce(detail)
    });
    const first = workflow.start();
    workflow.dispose();
    await workflow.start();
    const current = workflow.getSnapshot();
    old.resolve({ ...detail, battles: [] });
    await first;
    expect(workflow.getSnapshot()).toBe(current);
  });

  it('ignores a disposed deletion result without cancelling its actual outcome or releasing a new action', async () => {
    const old = deferred<HistoryRunDetail>();
    const next = deferred<void>();
    const { workflow } = setup({
      deleteBattleVideo: vi.fn().mockReturnValue(old.promise),
      revealRunScreenshot: vi.fn().mockReturnValue(next.promise)
    });
    await workflow.start();
    const deletion = workflow.intents.deleteVideo('battle-1', 'v');
    workflow.dispose();
    await workflow.start();
    const screenshot = workflow.intents.revealScreenshot();
    old.resolve({ ...detail, battles: [] });
    expect((await deletion).ok).toBe(true);
    expect(workflow.getSnapshot()).toMatchObject({
      detail,
      busy: true,
      screenshot: { running: true }
    });
    next.resolve();
    await screenshot;
    expect(workflow.getSnapshot().busy).toBe(false);
  });
});
