import { expect, test } from 'vitest';

import { formatWhatsNewBulletHtml } from './whats-new-format.ts';

test('formatWhatsNewBulletHtml escapes unsafe markup before key decoration', () => {
  expect(formatWhatsNewBulletHtml('<script>alert("x")</script>')).toBe(
    '&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;'
  );
});

test('formatWhatsNewBulletHtml wraps supported key names in kbd tags', () => {
  expect(formatWhatsNewBulletHtml('Press Ctrl Shift F8 or Enter')).toBe(
    'Press <kbd class="update-key">Ctrl</kbd> <kbd class="update-key">Shift</kbd> <kbd class="update-key">F8</kbd> or <kbd class="update-key">Enter</kbd>'
  );
});
