import { describe, expect, it } from 'vitest';
import { formatMessage, messages } from './messages';

describe('messages catalog', () => {
  it('defines the same keys for every locale', () => {
    const zhKeys = Object.keys(messages.zh).sort();
    const enKeys = Object.keys(messages.en).sort();
    expect(enKeys).toEqual(zhKeys);
  });

  it('returns a different string per locale for the same key', () => {
    expect(formatMessage('zh', 'navInstall')).toBe('安装');
    expect(formatMessage('en', 'navInstall')).toBe('Install');
  });

  it('provides localized History empty-state guidance and actions', () => {
    expect(messages.zh.historyEmptyDescription).toContain('The Bazaar');
    expect(messages.en.historyEmptyDescription).toContain('The Bazaar');
    expect(messages.zh.historyEmptyRefresh).not.toBe(
      messages.en.historyEmptyRefresh
    );
    expect(messages.zh.historyEmptyInstall).not.toBe(
      messages.en.historyEmptyInstall
    );
  });

  it('names the same OS surface the recovery step tells the user to open', () => {
    expect(messages.zh.historyEndGameProcessFailedWindows).toContain(
      '任务管理器'
    );
    expect(messages.zh.historyEndGameProcessFailedMac).toContain('活动监视器');
    expect(messages.en.updaterProblemRestartFailedWindows).toContain(
      'Start menu'
    );
    expect(messages.en.updaterProblemRestartFailedMac).toContain(
      'Applications'
    );
  });
});

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
