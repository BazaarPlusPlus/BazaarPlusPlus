// @vitest-environment jsdom

import { afterEach, describe, expect, it } from 'vitest';
import { hostPlatform, isMacPlatform, isWindowsPlatform } from './platform';

const originalUserAgent = Object.getOwnPropertyDescriptor(
  navigator,
  'userAgent'
);

function stubUserAgent(userAgent: string) {
  Object.defineProperty(navigator, 'userAgent', {
    value: userAgent,
    configurable: true
  });
}

afterEach(() => {
  if (originalUserAgent) {
    Object.defineProperty(navigator, 'userAgent', originalUserAgent);
  }
});

describe('hostPlatform', () => {
  it('reads windows, macOS and Linux out of the webview user agent', () => {
    stubUserAgent(
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
    );
    expect(hostPlatform()).toBe('windows');
    expect(isWindowsPlatform()).toBe(true);
    expect(isMacPlatform()).toBe(false);

    stubUserAgent(
      'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15'
    );
    expect(hostPlatform()).toBe('macos');
    expect(isMacPlatform()).toBe(true);
    expect(isWindowsPlatform()).toBe(false);

    stubUserAgent('Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/605.1.15');
    expect(hostPlatform()).toBe('linux');
    expect(isWindowsPlatform()).toBe(false);
    expect(isMacPlatform()).toBe(false);
  });
});
