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
});
