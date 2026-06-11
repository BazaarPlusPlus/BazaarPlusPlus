import { describe, expect, it } from 'vitest';
import { optionalStripPreviewUrl } from './stripPreview';

describe('optionalStripPreviewUrl', () => {
  it('composes a stream-service strip URL', () => {
    expect(
      optionalStripPreviewUrl('http://127.0.0.1:17654', '/images/shot-1/strip')
    ).toBe('http://127.0.0.1:17654/images/shot-1/strip');
  });

  it('returns null when the preview URL cannot be safely composed', () => {
    expect(optionalStripPreviewUrl(null, '/images/shot-1/strip')).toBeNull();
    expect(
      optionalStripPreviewUrl('http://127.0.0.1:17654', 'file:///tmp/shot.png')
    ).toBeNull();
  });
});
