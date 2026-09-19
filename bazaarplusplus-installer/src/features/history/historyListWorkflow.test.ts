import { describe, expect, it, vi } from 'vitest';
import {
  emptyHistoryRunList,
  idleStreamStatus
} from '../../api/previewDefaults';
import type { HistoryRunList, StreamServiceStatus } from '../../types/backend';
import { createHistoryListWorkflow } from './historyListWorkflow';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}

function pageData(page = 1, total = 101): HistoryRunList {
  const offset = (page - 1) * 50;
  return {
    ...emptyHistoryRunList,
    summary: { ...emptyHistoryRunList.summary, runs: total },
    runs: Array.from(
      { length: Math.max(0, Math.min(50, total - offset)) },
      (_, index) => ({
        run_id: `run-${offset + index + 1}`,
        hero: 'Vanessa',
        game_mode: 'Ranked',
        started_at_utc: '2026-09-12T10:00:00Z',
        ended_at_utc: null,
        result: 'active',
        victories: 1,
        losses: 0,
        final_day: 1,
        final_player_rank: null,
        final_player_rating: null,
        screenshot_id: null,
        strip_url: null
      })
    )
  };
}

const readFailure = {
  code: 'history_read_failed',
  params: { operation: 'list_runs' },
  diagnostic: 'database is locked'
};
const runningPreview: StreamServiceStatus = {
  ...idleStreamStatus,
  running: true,
  base_url: 'http://127.0.0.1:17654'
};

function fixture(initialPage = 1) {
  const commands = {
    listHistoryRuns: vi.fn(async (_limit = 50, offset = 0) =>
      pageData(offset / 50 + 1)
    ),
    endGameProcess: vi.fn(async () => true),
    getStreamStatus: vi.fn(async () => idleStreamStatus)
  };
  const replacePage = vi.fn();
  const workflow = createHistoryListWorkflow(commands, {
    initialPage,
    replacePage
  });
  return { commands, replacePage, workflow };
}

describe('History List workflow', () => {
  it('refreshes the current page through a callback retained by cleanup', async () => {
    const { commands, workflow } = fixture();
    await workflow.start();
    const onCleanupCompleted = workflow.intents.refresh;
    await workflow.selectPage(2);
    await onCleanupCompleted();
    expect(commands.listHistoryRuns).toHaveBeenLastCalledWith(50, 50);
    expect(workflow.getSnapshot().state).toMatchObject({ data: pageData(2) });
  });

  it('publishes empty only after a successful read, including a failed-read retry', async () => {
    const { commands, workflow } = fixture();
    commands.listHistoryRuns.mockRejectedValueOnce(readFailure);
    expect(workflow.getSnapshot().state.phase).toBe('initial-loading');
    await workflow.start();
    expect(workflow.getSnapshot().state).toMatchObject({
      phase: 'blocking-failure',
      problem: { code: 'history_read_failed' }
    });
    commands.listHistoryRuns.mockResolvedValueOnce(emptyHistoryRunList);
    await workflow.intents.refresh();
    expect(workflow.getSnapshot()).toMatchObject({
      state: { phase: 'ready-empty', data: emptyHistoryRunList },
      busy: false,
      pagination: { page: 1, pageCount: 1, start: 0, end: 0, total: 0 }
    });
  });

  it('retains successful data through refresh and refresh failure', async () => {
    const { commands, workflow } = fixture();
    await workflow.start();
    const request = deferred<HistoryRunList>();
    commands.listHistoryRuns.mockReturnValueOnce(request.promise);
    const refresh = workflow.intents.refresh();
    expect(workflow.getSnapshot()).toMatchObject({
      state: {
        phase: 'ready-content',
        data: pageData(),
        refresh: { phase: 'refreshing' }
      },
      busy: true
    });
    request.reject(readFailure);
    await refresh;
    expect(workflow.getSnapshot()).toMatchObject({
      state: {
        phase: 'ready-content',
        data: pageData(),
        refresh: { phase: 'failed' }
      },
      busy: false
    });
  });

  it.each(['success', 'failure'])(
    'rejects a late %s from the previous page',
    async (outcome) => {
      const { commands, replacePage, workflow } = fixture();
      await workflow.start();
      const oldRequest = deferred<HistoryRunList>();
      commands.listHistoryRuns.mockReturnValueOnce(oldRequest.promise);
      const oldRefresh = workflow.intents.refresh();
      const nextRequest = deferred<HistoryRunList>();
      commands.listHistoryRuns.mockReturnValueOnce(nextRequest.promise);
      const changingPage = workflow.selectPage(2);
      expect(workflow.getSnapshot().state).toEqual({
        phase: 'initial-loading'
      });
      nextRequest.resolve(pageData(2));
      await changingPage;
      if (outcome === 'success') oldRequest.resolve(pageData(1, 1));
      else oldRequest.reject(readFailure);
      await oldRefresh;
      expect(workflow.getSnapshot()).toMatchObject({
        state: { phase: 'ready-content', data: pageData(2) },
        pagination: { page: 2, start: 51, end: 100, total: 101 }
      });
      expect(replacePage).not.toHaveBeenCalled();
    }
  );

  it('keeps a changed-page failure blocking and retries that same page', async () => {
    const { commands, workflow } = fixture();
    await workflow.start();
    commands.listHistoryRuns.mockRejectedValueOnce(readFailure);
    await workflow.selectPage(2);
    expect(workflow.getSnapshot().state.phase).toBe('blocking-failure');
    expect(workflow.getSnapshot().state).not.toHaveProperty('data');
    await workflow.intents.refresh();
    expect(commands.listHistoryRuns).toHaveBeenLastCalledWith(50, 50);
    expect(workflow.getSnapshot().state).toMatchObject({ data: pageData(2) });
  });

  it.each(['success', 'failure'])(
    'rejects a late %s from a superseded refresh on the same page',
    async (outcome) => {
      const { commands, workflow } = fixture();
      await workflow.start();
      const request = deferred<HistoryRunList>();
      commands.listHistoryRuns.mockReturnValueOnce(request.promise);
      const oldRefresh = workflow.intents.refresh();
      commands.listHistoryRuns.mockResolvedValueOnce(pageData(1, 120));
      await workflow.intents.refresh();
      if (outcome === 'success') request.resolve(emptyHistoryRunList);
      else request.reject(readFailure);
      await oldRefresh;
      expect(workflow.getSnapshot().state).toMatchObject({
        phase: 'ready-content',
        data: pageData(1, 120),
        refresh: { phase: 'idle' }
      });
    }
  );

  it('corrects an out-of-range page before publishing its rows', async () => {
    const { commands, replacePage, workflow } = fixture(5);
    commands.listHistoryRuns.mockImplementation(async (_limit, offset) =>
      pageData((offset ?? 0) / 50 + 1, 52)
    );
    await workflow.start();
    expect(commands.listHistoryRuns.mock.calls).toEqual([
      [50, 200],
      [50, 50]
    ]);
    expect(replacePage).toHaveBeenCalledExactlyOnceWith(2);
    expect(workflow.getSnapshot()).toMatchObject({
      state: { phase: 'ready-content', data: pageData(2, 52) },
      pagination: {
        page: 2,
        pageCount: 2,
        start: 51,
        end: 52,
        total: 52,
        nextDisabled: true
      }
    });
    await workflow.selectPage(2);
    expect(commands.listHistoryRuns).toHaveBeenCalledTimes(2);
    expect(commands.getStreamStatus).toHaveBeenCalledOnce();
  });

  it('returns an emptied history to page 1 without a redirect loop', async () => {
    const { commands, replacePage, workflow } = fixture(3);
    commands.listHistoryRuns.mockResolvedValue(emptyHistoryRunList);
    await workflow.start();
    expect(replacePage).toHaveBeenCalledExactlyOnceWith(1);
    expect(commands.listHistoryRuns.mock.calls).toEqual([
      [50, 100],
      [50, 0]
    ]);
    expect(workflow.getSnapshot()).toMatchObject({
      state: { phase: 'ready-empty' },
      pagination: { page: 1, pageCount: 1 }
    });
  });

  it('retries the corrected page if its read fails after a total-count change', async () => {
    const { commands, workflow } = fixture(5);
    commands.listHistoryRuns
      .mockResolvedValueOnce(pageData(5, 52))
      .mockRejectedValueOnce(readFailure);
    await workflow.start();
    expect(workflow.getSnapshot()).toMatchObject({
      state: { phase: 'blocking-failure' },
      pagination: { page: 2 }
    });
    commands.listHistoryRuns.mockResolvedValueOnce(pageData(2, 52));
    await workflow.intents.refresh();
    expect(commands.listHistoryRuns).toHaveBeenLastCalledWith(50, 50);
    expect(workflow.getSnapshot().state).toMatchObject({
      data: pageData(2, 52)
    });
  });

  it.each([
    ['terminated', true],
    ['already-exited', false],
    ['failed', null]
  ] as const)(
    'refreshes the current page after recovery is %s',
    async (outcome, result) => {
      const { commands, workflow } = fixture();
      await workflow.start();
      const request = deferred<boolean>();
      commands.endGameProcess.mockReturnValueOnce(request.promise);
      const recovery = workflow.intents.endLeftoverGameProcess();
      expect(workflow.intents.endLeftoverGameProcess()).toBe(recovery);
      expect(workflow.getSnapshot().endingGameProcess).toBe(true);
      await workflow.selectPage(2);
      await workflow.selectPage(3);
      if (result === null) request.reject(new Error('process unavailable'));
      else request.resolve(result);
      await expect(recovery).resolves.toBe(outcome);
      expect(commands.endGameProcess).toHaveBeenCalledOnce();
      expect(commands.listHistoryRuns).toHaveBeenLastCalledWith(50, 100);
      expect(workflow.getSnapshot()).toMatchObject({
        state: { data: pageData(3) },
        endingGameProcess: false,
        pagination: { page: 3, start: 101, end: 101 }
      });
    }
  );

  it('keeps list reads independent of slow or failed thumbnail discovery', async () => {
    const { commands, workflow } = fixture();
    const request = deferred<StreamServiceStatus>();
    commands.getStreamStatus.mockReturnValueOnce(request.promise);
    const starting = workflow.start();
    await Promise.resolve();
    expect(workflow.getSnapshot()).toMatchObject({
      state: { phase: 'ready-content', data: pageData() },
      busy: false
    });
    request.reject(new Error('preview offline'));
    await starting;
    expect(workflow.getSnapshot()).toMatchObject({
      state: { phase: 'ready-content', refresh: { phase: 'idle' } },
      previewProblem: { code: 'history_preview_unavailable' }
    });
  });

  it('ignores stale thumbnail discovery after a newer refresh', async () => {
    const { commands, workflow } = fixture();
    const request = deferred<StreamServiceStatus>();
    commands.getStreamStatus.mockReturnValueOnce(request.promise);
    const starting = workflow.start();
    commands.getStreamStatus.mockResolvedValueOnce(runningPreview);
    await workflow.intents.refresh();
    const current = workflow.getSnapshot();
    expect(current.previewProblem).toBeNull();
    const run = {
      ...pageData().runs[0],
      strip_url: '/history/strips/run-1.webp'
    };
    expect(current.previewUrl(run)).toBe(
      'http://127.0.0.1:17654/history/strips/run-1.webp'
    );
    request.resolve(idleStreamStatus);
    await starting;
    expect(workflow.getSnapshot()).toBe(current);
  });

  it('rejects old list, preview and recovery completions after a lifecycle restart', async () => {
    const { commands, replacePage, workflow } = fixture(2);
    const oldList = deferred<HistoryRunList>();
    const oldPreview = deferred<StreamServiceStatus>();
    const oldProcess = deferred<boolean>();
    commands.listHistoryRuns.mockReturnValueOnce(oldList.promise);
    commands.getStreamStatus.mockReturnValueOnce(oldPreview.promise);
    commands.endGameProcess.mockReturnValueOnce(oldProcess.promise);
    const oldStart = workflow.start();
    const oldRecovery = workflow.intents.endLeftoverGameProcess();
    await Promise.resolve();
    workflow.dispose();
    commands.getStreamStatus.mockResolvedValueOnce(runningPreview);
    await workflow.start();
    const newProcess = deferred<boolean>();
    commands.endGameProcess.mockReturnValueOnce(newProcess.promise);
    const newRecovery = workflow.intents.endLeftoverGameProcess();
    const current = workflow.getSnapshot();
    const changed = vi.fn();
    const unsubscribe = workflow.subscribe(changed);

    oldList.resolve(pageData(1, 1));
    oldPreview.resolve(idleStreamStatus);
    oldProcess.resolve(true);
    await Promise.all([oldStart, oldRecovery]);

    expect(workflow.getSnapshot()).toBe(current);
    expect(workflow.getSnapshot().endingGameProcess).toBe(true);
    expect(changed).not.toHaveBeenCalled();
    expect(replacePage).not.toHaveBeenCalled();
    expect(commands.listHistoryRuns).toHaveBeenCalledTimes(2);
    newProcess.resolve(true);
    await newRecovery;
    expect(changed).toHaveBeenCalled();
    expect(commands.listHistoryRuns).toHaveBeenLastCalledWith(50, 50);
    expect(workflow.getSnapshot().endingGameProcess).toBe(false);
    unsubscribe();
    changed.mockClear();
    await workflow.intents.refresh();
    expect(changed).not.toHaveBeenCalled();
  });

  it('does not start effects while stopped or start duplicate active lifecycles', async () => {
    const { commands, workflow } = fixture();
    await workflow.intents.refresh();
    await expect(workflow.intents.endLeftoverGameProcess()).resolves.toBe(
      'failed'
    );
    expect(commands.listHistoryRuns).not.toHaveBeenCalled();
    expect(commands.endGameProcess).not.toHaveBeenCalled();
    await workflow.start();
    await workflow.start();
    expect(commands.listHistoryRuns).toHaveBeenCalledOnce();
    workflow.dispose();
    await workflow.intents.refresh();
    await expect(workflow.intents.endLeftoverGameProcess()).resolves.toBe(
      'failed'
    );
    expect(commands.listHistoryRuns).toHaveBeenCalledOnce();
    expect(commands.endGameProcess).not.toHaveBeenCalled();
  });
});
