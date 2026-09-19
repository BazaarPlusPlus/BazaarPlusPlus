import { describe, expect, it } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import {
  formatBytes,
  formatDateTime,
  formatDuration,
  formatRunResultLabel
} from './format';

describe('History run outcomes', () => {
  it.each([
    [0, 3, 'misfortune', '惨淡旅程', 'MISFORTUNE JOURNEY'],
    [3, 3, 'misfortune', '惨淡旅程', 'MISFORTUNE JOURNEY'],
    [4, 3, 'bronze', '青铜胜利', 'BRONZE VICTORY'],
    [6, 3, 'bronze', '青铜胜利', 'BRONZE VICTORY'],
    [7, 3, 'silver', '白银胜利', 'SILVER VICTORY'],
    [9, 3, 'silver', '白银胜利', 'SILVER VICTORY'],
    [10, 1, 'gold', '黄金胜利', 'GOLD VICTORY'],
    [10, 3, 'gold', '黄金胜利', 'GOLD VICTORY'],
    [11, 0, 'gold', '黄金胜利', 'GOLD VICTORY'],
    [10, 0, 'diamond', '钻石胜利', 'DIAMOND VICTORY'],
    [10, null, 'diamond', '钻石胜利', 'DIAMOND VICTORY'],
    [null, null, 'misfortune', '惨淡旅程', 'MISFORTUNE JOURNEY']
  ] as const)(
    'presents a completed %s-win, %s-loss run with its mod tier and localized label',
    (victories, losses, tier, zh, en) => {
      const outcome = formatRunResultLabel({
        result: (victories ?? 0) >= 10 ? 'win' : 'loss',
        victories,
        losses
      });

      expect(outcome.tier).toBe(tier);
      expect(formatMessage('zh', outcome.key)).toBe(zh);
      expect(formatMessage('en', outcome.key)).toBe(en);
    }
  );

  it.each([
    ['abandoned', '中途放弃'],
    ['in_progress', '正在进行'],
    ['unknown', '正在进行']
  ])('keeps %s runs untiered even with ten wins', (result, label) => {
    const outcome = formatRunResultLabel({
      result,
      victories: 10,
      losses: 0
    });

    expect(outcome.tier).toBeUndefined();
    expect(outcome.state).toBe(result === 'abandoned' ? 'abandoned' : 'active');
    expect(formatMessage('zh', outcome.key)).toBe(label);
  });
});

describe('History date formatting', () => {
  it('follows the selected locale', () => {
    const value = '2026-01-02T15:04:00';

    expect(formatDateTime(value, 'zh')).not.toBe(formatDateTime(value, 'en'));
    expect(formatDateTime(value, 'en')).toMatch(/AM|PM/);
  });

  it('formats video durations and sizes through the selected locale helpers', () => {
    expect(formatDuration(90_000, 'zh')).toContain('1');
    expect(formatDuration(90_000, 'en')).toMatch(/min/i);
    expect(formatBytes(1_572_864, 'zh')).toContain('1.5');
    expect(formatBytes(1_572_864, 'en')).toContain('1.5');
    expect(formatDuration(null, 'en')).toBe('-');
    expect(formatBytes(null, 'en')).toBe('-');
  });
});
