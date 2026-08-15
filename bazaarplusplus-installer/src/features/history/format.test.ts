import { describe, expect, it } from 'vitest';
import { formatBytes, formatDateTime, formatDuration } from './format';

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
