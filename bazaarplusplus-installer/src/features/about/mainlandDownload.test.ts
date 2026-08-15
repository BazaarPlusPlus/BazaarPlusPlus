import { describe, expect, it } from 'vitest';
import {
  buildMainlandDownloadUrl,
  detectMainlandDownloadPlatform
} from './mainlandDownload';

describe('mainland installer downloads', () => {
  it('detects the shipped installer platform from its user agent', () => {
    expect(detectMainlandDownloadPlatform('Windows NT 10.0')).toBe('windows');
    expect(detectMainlandDownloadPlatform('Macintosh; Intel Mac OS X')).toBe(
      'mac'
    );
  });

  it('matches the website mirror URL convention for each platform', () => {
    expect(buildMainlandDownloadUrl('windows', '5.1.0')).toBe(
      'https://cauyxy.lanzout.com/bppwin510'
    );
    expect(buildMainlandDownloadUrl('mac', '5.1.0')).toBe(
      'https://cauyxy.lanzout.com/bppmac510'
    );
  });
});
