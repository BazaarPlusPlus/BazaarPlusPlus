import { readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, test } from 'vitest';

const projectRoot = resolve(import.meta.dirname, '..');

describe('icon assets', () => {
  test('does not advertise the app logo as an Apple touch icon', () => {
    const html = readFileSync(resolve(projectRoot, 'index.html'), 'utf8');

    expect(html).not.toContain('rel="apple-touch-icon"');
  });

  test('keeps the shared BazaarPlusPlus logo asset small enough for LCP use', () => {
    const icon = statSync(resolve(projectRoot, 'public/bazaarplusplus-icon.webp'));

    expect(icon.size).toBeLessThanOrEqual(10_000);
  });

  test('keeps the favicon lightweight', () => {
    const icon = statSync(resolve(projectRoot, 'public/favicon.webp'));

    expect(icon.size).toBeLessThanOrEqual(5_000);
  });
});
