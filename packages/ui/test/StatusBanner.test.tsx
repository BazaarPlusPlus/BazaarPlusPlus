import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vite-plus/test';
import { StatusBanner } from '../src/index';

describe('StatusBanner', () => {
  it('announces errors assertively as alerts', () => {
    const html = renderToStaticMarkup(
      <StatusBanner tone="error" message="Something failed" />
    );

    expect(html).toContain('role="alert"');
    expect(html).toContain('aria-live="assertive"');
    expect(html).toContain('Something failed');
  });
});
