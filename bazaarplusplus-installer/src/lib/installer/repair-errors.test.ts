import { test, expect } from 'vitest';

import { describeRepairError, parseRepairError } from './repair-errors.ts';

const localized = (zh: string, en: string) => en || zh;

test('parseRepairError detects the game-running classification', () => {
  expect(parseRepairError('bpp_data_reset_blocked_by_game')).toEqual({
    kind: 'game_running'
  });
});

test('parseRepairError extracts paths from a partial-failure message', () => {
  const error = parseRepairError(
    'bpp_data_reset_partial_failure:C:/Games/The Bazaar/BazaarPlusPlus/bazaarplusplus.db\u{1f}C:/Games/The Bazaar/BazaarPlusPlus/Identity/observation.v1.json'
  );

  expect(error).toEqual({
    kind: 'partial_failure',
    failedPaths: [
      'C:/Games/The Bazaar/BazaarPlusPlus/bazaarplusplus.db',
      'C:/Games/The Bazaar/BazaarPlusPlus/Identity/observation.v1.json'
    ]
  });
});

test('parseRepairError preserves an unknown plain-text message', () => {
  const error = parseRepairError(new Error('some random failure'));
  expect(error).toEqual({ kind: 'unknown', message: 'some random failure' });
});

test('parseRepairError tolerates an empty partial-failure list', () => {
  const error = parseRepairError('bpp_data_reset_partial_failure:');
  expect(error).toEqual({ kind: 'partial_failure', failedPaths: [] });
});

test('describeRepairError returns retry copy for game_running', () => {
  const copy = describeRepairError({ kind: 'game_running' }, localized);
  expect(copy.title).toMatch(/Close The Bazaar/);
  expect(copy.retryLabel).toMatch(/retry/i);
  expect(copy.paths).toBeUndefined();
});

test('describeRepairError surfaces the path list for partial_failure', () => {
  const copy = describeRepairError(
    {
      kind: 'partial_failure',
      failedPaths: ['/path/a', '/path/b']
    },
    localized
  );

  expect(copy.paths).toEqual(['/path/a', '/path/b']);
  expect(copy.pathListLabel).toBeTruthy();
});
