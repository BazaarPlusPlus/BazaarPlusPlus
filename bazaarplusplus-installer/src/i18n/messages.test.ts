import { describe, expect, it } from 'vitest';
import { formatMessage } from './messages';

describe('formatMessage', () => {
  it('returns the raw message when no params are given', () => {
    expect(formatMessage('en', 'updateInstall')).toBe('Download & Install');
  });

  it('interpolates named placeholders', () => {
    expect(formatMessage('en', 'updateModalBody', { version: '4.1.0' })).toBe(
      'BazaarPlusPlus 4.1.0 is available.'
    );
    expect(formatMessage('zh', 'streamWindowOffset', { count: 3 })).toBe(
      '向前补 3 条记录'
    );
  });

  it('selects the English singular or plural form from the count', () => {
    expect(formatMessage('en', 'streamWindowOffset', { count: 1 })).toBe(
      'Back 1 record'
    );
    expect(formatMessage('en', 'streamWindowOffset', { count: 3 })).toBe(
      'Back 3 records'
    );
  });

  it('selects each count independently in one message', () => {
    expect(
      formatMessage('en', 'storageCleanupRunDataDone', {
        runs: 1,
        files: 4,
        size: '2 MB'
      })
    ).toBe('Deleted 1 run and 4 files, freed about 2 MB.');
    expect(
      formatMessage('en', 'storageCleanupRunDataDone', {
        runs: 2,
        files: 1,
        size: '2 MB'
      })
    ).toBe('Deleted 2 runs and 1 file, freed about 2 MB.');
  });

  it('leaves a plural token untouched when its count is absent', () => {
    expect(formatMessage('en', 'streamWindowOffset', { other: 1 })).toContain(
      '{count|record|records}'
    );
  });
});
