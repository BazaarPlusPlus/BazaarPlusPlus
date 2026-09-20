import { describe, expect, it } from 'vitest';
import { detectMainlandDownloadPlatform } from './mainlandDownload';

describe('mainland installer downloads', () => {
  it('detects the shipped installer platform from its user agent', () => {
    expect(detectMainlandDownloadPlatform('Windows NT 10.0')).toBe('windows');
    expect(detectMainlandDownloadPlatform('Macintosh; Intel Mac OS X')).toBe(
      'mac'
    );
  });
});
