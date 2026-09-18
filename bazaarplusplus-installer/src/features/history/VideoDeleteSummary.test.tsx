// @vitest-environment jsdom

import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { LocaleProvider } from '../../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../../i18n/messages';
import { VideoDeleteSummary } from './VideoDeleteSummary';

describe('video deletion target', () => {
  it.each(['zh', 'en'])(
    'identifies the battle in %s and keeps internal ids in collapsed diagnostics',
    (locale) => {
      localStorage.setItem(LOCALE_STORAGE_KEY, locale);
      const container = document.createElement('div');
      container.innerHTML = renderToStaticMarkup(
        <LocaleProvider>
          <VideoDeleteSummary
            target={{
              kind: 'delete-video',
              battleId: 'battle-123',
              videoId: 'video-456',
              runStartedAt: '2026-09-12T02:00:00Z',
              day: 7,
              opponent: 'WanderingMerchant · The Dragons',
              durationMs: 125000
            }}
          />
        </LocaleProvider>
      );
      const summary = container.querySelector('dl')!;
      expect(summary.textContent).toContain('09/12');
      expect(summary.textContent).toContain('7');
      expect(summary.textContent).toContain('WanderingMerchant · The Dragons');
      expect(summary.textContent).toContain(
        locale === 'zh' ? '2分钟 5秒' : '2 min 5 sec'
      );
      expect(summary.textContent).not.toContain('battle-123');
      const diagnostics = container.querySelector('details')!;
      expect(diagnostics.open).toBe(false);
      expect(diagnostics.textContent).toContain('battle-123');
      expect(diagnostics.textContent).toContain('video-456');
    }
  );
});
