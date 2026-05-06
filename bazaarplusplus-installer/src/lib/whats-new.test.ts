import { test, expect } from 'vitest';

import { resolveWhatsNewRelease, whatsNewReleases } from './whats-new.ts';

test('resolveWhatsNewRelease matches the requested version exactly', () => {
  const release = resolveWhatsNewRelease('2.3.6');

  expect(release.version).toBe('2.3.6');
  expect(release.sections[0]?.title.en).toBe('Ghost Battle Replay');
});

test('resolveWhatsNewRelease falls back to the latest known release', () => {
  const release = resolveWhatsNewRelease('9.9.9');

  expect(release.version).toBe(whatsNewReleases[0]?.version);
});
