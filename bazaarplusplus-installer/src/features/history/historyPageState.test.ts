import { describe, expect, it } from 'vitest';
import { emptyHistoryRunList } from '../../api/previewDefaults';
import { createUiProblem } from '../shared/problems';
import {
  initialHistoryPageState,
  reduceHistoryPageState,
  type HistoryPageState
} from './historyPageState';

const oneRun = {
  ...emptyHistoryRunList,
  summary: { ...emptyHistoryRunList.summary, runs: 1 },
  runs: [
    {
      run_id: 'run-1',
      hero: 'Vanessa',
      game_mode: 'Ranked',
      started_at_utc: '2026-01-02T15:04:00',
      ended_at_utc: null,
      last_seen_at_utc: '2026-01-02T15:04:00',
      result: 'active',
      victories: 1,
      losses: 0,
      final_day: 1,
      final_player_rank: null,
      final_player_rating: null,
      screenshot_id: null,
      strip_url: null,
      video_count: 0
    }
  ]
};

const readProblem = createUiProblem('history_read_failed', {
  params: { operation: 'list_runs' },
  diagnostic: 'database is locked'
});

function transition(
  state: HistoryPageState,
  event: Parameters<typeof reduceHistoryPageState>[1]
) {
  return reduceHistoryPageState(state, event);
}

describe('History page state', () => {
  it('shows empty only after a successful empty response', () => {
    const loading = transition(initialHistoryPageState, {
      type: 'request-started',
      requestId: 1
    });
    expect(loading.phase).toBe('initial-loading');

    const failed = transition(loading, {
      type: 'request-failed',
      requestId: 1,
      problem: readProblem
    });
    expect(failed.phase).toBe('blocking-failure');

    const retrying = transition(failed, {
      type: 'request-started',
      requestId: 2
    });
    const empty = transition(retrying, {
      type: 'request-succeeded',
      requestId: 2,
      data: emptyHistoryRunList
    });
    expect(empty.phase).toBe('ready-empty');
  });

  it('preserves loaded runs through refreshing and refresh failure', () => {
    const loaded = transition(
      { phase: 'initial-loading', requestId: 1 },
      { type: 'request-succeeded', requestId: 1, data: oneRun }
    );
    expect(loaded.phase).toBe('ready-content');

    const refreshing = transition(loaded, {
      type: 'request-started',
      requestId: 2
    });
    expect(refreshing).toMatchObject({
      phase: 'ready-content',
      data: oneRun,
      refresh: { phase: 'refreshing' }
    });

    const failed = transition(refreshing, {
      type: 'request-failed',
      requestId: 2,
      problem: readProblem
    });
    expect(failed).toMatchObject({
      phase: 'ready-content',
      data: oneRun,
      refresh: { phase: 'failed', problem: readProblem }
    });
  });

  it('ignores stale responses from an older request', () => {
    const current = transition(
      { phase: 'initial-loading', requestId: 2 },
      { type: 'request-succeeded', requestId: 1, data: emptyHistoryRunList }
    );
    expect(current).toEqual({ phase: 'initial-loading', requestId: 2 });
  });
});
