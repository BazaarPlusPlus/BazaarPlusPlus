import { describe, expect, it } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import {
  formatBytes,
  formatDateTime,
  formatDuration,
  formatHero,
  formatRank,
  formatRatingDelta,
  formatRunResultLabel
} from './format';

const zhT = (key: Parameters<typeof formatMessage>[1]) =>
  formatMessage('zh', key);
const enT = (key: Parameters<typeof formatMessage>[1]) =>
  formatMessage('en', key);

describe('History hero and rank labels', () => {
  it.each([
    ['The Dragons', '双龙', 'The Dragons'],
    ['TheDragons', '双龙', 'The Dragons'],
    ['Hero8', '双龙', 'The Dragons'],
    ['Vanessa', '凡妮莎', 'Vanessa'],
    ['Pygmalien', '皮格马利翁', 'Pygmalien'],
    ['Dooley', '杜利', 'Dooley'],
    ['Mak', '马克', 'Mak'],
    ['Jules', '朱尔斯', 'Jules'],
    ['Stelle', '斯黛拉', 'Stelle'],
    ['Karnok', '卡诺克', 'Karnok'],
    ['SomeNewHero', 'SomeNewHero', 'SomeNewHero']
  ])('presents hero %s in each locale', (hero, zh, en) => {
    expect(formatHero(hero, zhT)).toBe(zh);
    expect(formatHero(hero, enT)).toBe(en);
  });

  it.each([
    ['Legendary', '传奇', 'Legendary'],
    ['Gold 1', '黄金 1', 'Gold 1'],
    ['Diamond III', '钻石 III', 'Diamond III'],
    ['bronze', '青铜', 'Bronze'],
    ['Silver', '白银', 'Silver'],
    ['Masters', '大师', 'Master'],
    ['Mythic', 'Mythic', 'Mythic']
  ])('presents rank %s in each locale', (rank, zh, en) => {
    expect(formatRank(rank, zhT)).toBe(zh);
    expect(formatRank(rank, enT)).toBe(en);
  });
});

describe('History rating change', () => {
  it.each([
    [16, '+16'],
    [-18, '-18'],
    [0, '±0'],
    [null, null]
  ])('formats %s', (delta, expected) => {
    expect(formatRatingDelta(delta)).toBe(expected);
  });
});

describe('History run outcomes', () => {
  it.each([
    [0, 3, 'misfortune', '惨淡旅程', 'Misfortune journey'],
    [3, 3, 'misfortune', '惨淡旅程', 'Misfortune journey'],
    [4, 3, 'bronze', '青铜胜利', 'Bronze victory'],
    [6, 3, 'bronze', '青铜胜利', 'Bronze victory'],
    [7, 3, 'silver', '白银胜利', 'Silver victory'],
    [9, 3, 'silver', '白银胜利', 'Silver victory'],
    [10, 1, 'gold', '黄金胜利', 'Gold victory'],
    [10, 3, 'gold', '黄金胜利', 'Gold victory'],
    [11, 0, 'gold', '黄金胜利', 'Gold victory'],
    [10, 0, 'diamond', '钻石胜利', 'Diamond victory'],
    [10, null, 'diamond', '钻石胜利', 'Diamond victory'],
    [null, null, 'misfortune', '惨淡旅程', 'Misfortune journey']
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
