import { describe, expect, it } from 'vitest';
import type { HistoryRunDetail } from '../../types/backend';
import { createUiProblem } from '../shared/problems';
import {
  actionAvailability,
  actionProblemFor,
  beginRunDetailAction,
  completeRunDetailAction,
  initialRunDetailActionState,
  initialRunDetailPageState,
  reduceRunDetailPageState,
  failRunDetailAction,
  type RunDetailPageState
} from './runDetailPageState';

const detail: HistoryRunDetail = {
  run: {
    run_id: 'run-1',
    hero: 'Vanessa',
    game_mode: 'Ranked',
    started_at_utc: '2026-01-02T15:04:00Z',
    ended_at_utc: '2026-01-02T15:34:00Z',
    last_seen_at_utc: '2026-01-02T15:34:00Z',
    status: 'completed',
    result: 'win',
    victories: 10,
    losses: 2,
    final_day: 12,
    final_hour: 1,
    final_player_rank: 'Gold',
    final_player_rating: 1234,
    screenshot_id: 'shot-1',
    strip_url: null,
    video_count: 1,
    player_name: 'Player'
  },
  battles: []
};

const readProblem = createUiProblem('history_read_failed', {
  params: { operation: 'get_run_detail' },
  diagnostic: 'database is locked'
});
const actionProblem = createUiProblem('history_action_failed', {
  params: { operation: 'reveal_video' },
  diagnostic: 'video is missing'
});

function transition(
  state: RunDetailPageState,
  event: Parameters<typeof reduceRunDetailPageState>[1]
) {
  return reduceRunDetailPageState(state, event);
}

describe('Run Detail page state', () => {
  it('keeps initial loading, not found, blocking failure, and ready content distinct', () => {
    const loading = transition(initialRunDetailPageState, {
      type: 'request-started',
      requestId: 1
    });
    expect(loading.phase).toBe('initial-loading');

    expect(
      transition(loading, {
        type: 'request-succeeded',
        requestId: 1,
        data: null
      }).phase
    ).toBe('not-found');

    expect(
      transition(loading, {
        type: 'request-failed',
        requestId: 1,
        problem: readProblem
      }).phase
    ).toBe('blocking-failure');

    expect(
      transition(loading, {
        type: 'request-succeeded',
        requestId: 1,
        data: detail
      }).phase
    ).toBe('ready');
  });

  it('preserves usable detail through refresh failure and recovers on retry', () => {
    const loaded = transition(
      { phase: 'initial-loading', requestId: 1 },
      { type: 'request-succeeded', requestId: 1, data: detail }
    );
    const refreshing = transition(loaded, {
      type: 'request-started',
      requestId: 2
    });
    const failed = transition(refreshing, {
      type: 'request-failed',
      requestId: 2,
      problem: readProblem
    });

    expect(failed).toMatchObject({
      phase: 'ready',
      data: detail,
      refresh: { phase: 'failed', problem: readProblem }
    });

    const retrying = transition(failed, {
      type: 'request-started',
      requestId: 3
    });
    expect(retrying).toMatchObject({
      phase: 'ready',
      data: detail,
      refresh: { phase: 'refreshing' }
    });
    expect(
      transition(retrying, {
        type: 'request-succeeded',
        requestId: 3,
        data: detail
      })
    ).toMatchObject({ phase: 'ready', refresh: { phase: 'idle' } });
  });

  it('ignores a stale completion once a newer request owns the page', () => {
    expect(
      transition(
        { phase: 'initial-loading', requestId: 2 },
        { type: 'request-succeeded', requestId: 1, data: detail }
      )
    ).toEqual({ phase: 'initial-loading', requestId: 2 });
  });
});

describe('Run Detail action state', () => {
  it('visibly gates every action while the shared single-flight slot is occupied', () => {
    const running = beginRunDetailAction(
      initialRunDetailActionState,
      'screenshot'
    );

    expect(actionAvailability(running, 'screenshot')).toEqual({
      disabled: true,
      running: true
    });
    expect(actionAvailability(running, 'video:battle-1')).toEqual({
      disabled: true,
      running: false
    });
    expect(actionAvailability(running, 'delete:battle-1')).toEqual({
      disabled: true,
      running: false
    });
    expect(beginRunDetailAction(running, 'video:battle-1')).toBe(running);
    expect(
      actionAvailability(initialRunDetailActionState, 'video:battle-1', true)
    ).toEqual({ disabled: true, running: false });
  });

  it('attaches failures to their target and clears them when that target retries', () => {
    const running = beginRunDetailAction(
      initialRunDetailActionState,
      'video:battle-1'
    );
    const failed = failRunDetailAction(
      running,
      'video:battle-1',
      actionProblem
    );

    expect(actionProblemFor(failed, 'battle:battle-1')).toEqual({
      action: 'video:battle-1',
      problem: actionProblem
    });
    expect(actionProblemFor(failed, 'battle:battle-2')).toBeNull();
    expect(actionProblemFor(failed, 'screenshot')).toBeNull();

    const retrying = beginRunDetailAction(failed, 'video:battle-1');
    expect(actionProblemFor(retrying, 'battle:battle-1')).toBeNull();
    expect(
      completeRunDetailAction(retrying, 'video:battle-1').current
    ).toBeNull();
  });
});
