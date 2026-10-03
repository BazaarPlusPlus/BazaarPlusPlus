import { describe, expect, test } from 'vitest';

import { getSiteCopy } from '../src/content/site-copy';

function collectStrings(value: unknown): string[] {
  if (typeof value === 'string') {
    return [value];
  }

  if (Array.isArray(value)) {
    return value.flatMap(collectStrings);
  }

  if (value && typeof value === 'object') {
    return Object.values(value).flatMap(collectStrings);
  }

  return [];
}

describe('site copy', () => {
  test('uses Chinese punctuation in Chinese copy', () => {
    const strings = collectStrings(getSiteCopy('zh'));
    const stringsWithAsciiCommas = strings.filter((value) => value.includes(','));

    expect(stringsWithAsciiCommas).toEqual([]);
  });
});
