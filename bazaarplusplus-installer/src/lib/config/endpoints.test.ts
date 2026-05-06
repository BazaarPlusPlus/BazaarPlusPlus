import { expect, test } from 'vitest';

import { getTutorialUrl } from './endpoints.ts';

test('getTutorialUrl selects the English tutorial URL for English locale', () => {
  expect(getTutorialUrl('en')).toBe(
    'https://bazaarplusplus.com/tutorial?lang=en'
  );
});

test('getTutorialUrl selects the default tutorial URL for Chinese locale', () => {
  expect(getTutorialUrl('zh')).toBe('https://bazaarplusplus.com/tutorial');
});
