import { describe, expect, it } from 'vitest';
import { composeStripPreviewUrl, joinServiceUrl } from './http';

describe('stream HTTP URL helpers', () => {
  it('joins a local service base URL and relative route without duplicate slashes', () => {
    expect(
      joinServiceUrl('http://127.0.0.1:17654/', '/api/stream/records')
    ).toBe('http://127.0.0.1:17654/api/stream/records');
  });

  it('composes strip preview URLs from relative strip paths only', () => {
    expect(
      composeStripPreviewUrl('http://127.0.0.1:17654', '/images/abc/strip')
    ).toBe('http://127.0.0.1:17654/images/abc/strip');
  });

  it('rejects full image URLs at the React boundary', () => {
    expect(() =>
      composeStripPreviewUrl('http://127.0.0.1:17654', 'file:///tmp/full.png')
    ).toThrow('relative strip URL');
  });
});
